#!/usr/bin/env python3
"""Neutralize agy's "[Notice] ... stopped due to server restart" context injection.

agy-acp runs every turn as a fresh `agy --conversation <id> -p ...` process. On each
such start agy persists a system message ("[Notice] All your subagents and background
tasks have been stopped due to server restart ...") into the conversation, so the
notice piles up in the model context, one copy per turn.

The message is produced by cortex.(*CascadeManager).sendRecoveryMessage, a void Go
function. The binary is stripped but keeps .gopclntab, so the function is located
without hard-coded offsets: find the notice strings, find the code that references
them, resolve the enclosing function through the pclntab and verify its name and its
Go stack-check prologue. The function's first instruction is then replaced with `ret`,
turning it into a no-op.

Function entries in the pclntab are offsets from runtime.text. agy is a PIE linked by
an external linker with C++ code placed before the Go code, so runtime.text is NOT the
start of the .text section, and pcHeader.textStart is left as 0 in the file. In that
case the real value is taken from moduledata.text, which is filled in at load time by
an R_*_RELATIVE dynamic relocation whose addend holds the address.

Supported targets: ELF64 little-endian, x86-64 and AArch64 (Go >= 1.20 pclntab).
Exits non-zero (failing the image build) if anything does not match expectations,
so an agy upgrade that changes the layout is never patched blindly.
"""

import re
import struct
import sys

EM_X86_64 = 0x3E
EM_AARCH64 = 0xB7
PT_LOAD = 1
PF_X = 1
SHT_RELA = 4
RELATIVE_RELOCATION_TYPES = {EM_X86_64: 8, EM_AARCH64: 1027}

# Leading moduledata fields (stable since Go 1.18): pcHeader *pcHeader @0x00,
# funcnametab []byte @0x08, ..., findfunctab @0x98, minpc @0xa0, maxpc @0xa8, text @0xb0.
MODULEDATA_FUNCNAMETAB_OFFSET = 0x08
MODULEDATA_TEXT_OFFSET = 0xB0

EXPECTED_FUNCTION_SUFFIX = "sendRecoveryMessage"
NOTICE_PREFIX = b"[Notice] All your "
NOTICE_MARKER = b"have been stopped due to"

X86_RET = b"\xc3"
ARM64_RET = struct.pack("<I", 0xD65F03C0)

# Go stack-split check at function entry:
# x86-64: [lea r12, [rsp-N]] ; cmp rsp|r12, [r14+0x10]
# arm64:  ldr x16, [x28, #0x10]
X86_GO_PROLOGUE = re.compile(
    rb"(?:\x49\x3b\x66\x10|\x4c\x8d\x64\x24.\x4d\x3b\x66\x10|\x4c\x8d\xa4\x24....\x4d\x3b\x66\x10)",
    re.DOTALL,
)
ARM64_GO_PROLOGUE = struct.pack("<I", 0xF9400B90)


class PatchError(Exception):
    pass


class Segment:
    def __init__(self, offset, vaddr, filesz, flags):
        self.offset = offset
        self.vaddr = vaddr
        self.filesz = filesz
        self.flags = flags

    def contains_offset(self, offset):
        return self.offset <= offset < self.offset + self.filesz

    def contains_vaddr(self, vaddr):
        return self.vaddr <= vaddr < self.vaddr + self.filesz


def parse_elf(data):
    if data[:4] != b"\x7fELF" or data[4] != 2 or data[5] != 1:
        raise PatchError("not an ELF64 little-endian binary")
    machine = struct.unpack_from("<H", data, 0x12)[0]
    phoff = struct.unpack_from("<Q", data, 0x20)[0]
    phentsize, phnum = struct.unpack_from("<HH", data, 0x36)
    segments = []
    for i in range(phnum):
        p_type, p_flags, p_offset, p_vaddr, _, p_filesz, _, _ = struct.unpack_from(
            "<IIQQQQQQ", data, phoff + i * phentsize
        )
        if p_type == PT_LOAD:
            segments.append(Segment(p_offset, p_vaddr, p_filesz, p_flags))
    if not segments:
        raise PatchError("no PT_LOAD segments")
    return machine, segments


def iter_relative_relocations(data, machine):
    relative_type = RELATIVE_RELOCATION_TYPES[machine]
    shoff = struct.unpack_from("<Q", data, 0x28)[0]
    shentsize, shnum = struct.unpack_from("<HH", data, 0x3A)
    view = memoryview(data)
    for i in range(shnum):
        header = shoff + i * shentsize
        sh_type = struct.unpack_from("<I", data, header + 4)[0]
        if sh_type != SHT_RELA:
            continue
        sh_offset, sh_size = struct.unpack_from("<QQ", data, header + 0x18)
        for r_offset, r_info, r_addend in struct.iter_unpack("<QQq", view[sh_offset : sh_offset + sh_size]):
            if r_info & 0xFFFFFFFF == relative_type:
                yield r_offset, r_addend


def find_moduledata_text(data, machine, pclntab_vaddr, funcnametab_vaddr):
    """Return moduledata.text of the module whose pcHeader is at pclntab_vaddr (PIE only)."""
    candidates = {offset for offset, addend in iter_relative_relocations(data, machine) if addend == pclntab_vaddr}
    if not candidates:
        raise PatchError(f"no relocated pointer to pclntab {pclntab_vaddr:#x}; cannot locate moduledata")
    wanted = set()
    for slot in candidates:
        wanted.update((slot + MODULEDATA_FUNCNAMETAB_OFFSET, slot + MODULEDATA_TEXT_OFFSET))
    pointers = {offset: addend for offset, addend in iter_relative_relocations(data, machine) if offset in wanted}
    texts = {
        pointers[slot + MODULEDATA_TEXT_OFFSET]
        for slot in candidates
        if pointers.get(slot + MODULEDATA_FUNCNAMETAB_OFFSET) == funcnametab_vaddr
        and slot + MODULEDATA_TEXT_OFFSET in pointers
    }
    if len(texts) != 1:
        raise PatchError(f"expected exactly one moduledata for pclntab {pclntab_vaddr:#x}, found text values {sorted(map(hex, texts))}")
    return texts.pop()


def offset_to_vaddr(segments, offset):
    for seg in segments:
        if seg.contains_offset(offset):
            return seg.vaddr + offset - seg.offset
    raise PatchError(f"file offset {offset:#x} is not mapped")


def vaddr_to_offset(segments, vaddr):
    for seg in segments:
        if seg.contains_vaddr(vaddr):
            return seg.offset + vaddr - seg.vaddr
    raise PatchError(f"virtual address {vaddr:#x} is not mapped")


def find_notice_strings(data):
    offsets = []
    for match in re.finditer(re.escape(NOTICE_PREFIX), data):
        window = data[match.start() : match.start() + 160]
        if NOTICE_MARKER in window:
            offsets.append(match.start())
    return offsets


def find_x86_references(data, segments, target_vaddrs):
    pattern = re.compile(rb"(?=[\x48\x4c]\x8d[\x05\x0d\x15\x1d\x25\x2d\x35\x3d])", re.DOTALL)
    found = {}
    for seg in (s for s in segments if s.flags & PF_X):
        end = seg.offset + seg.filesz
        for match in pattern.finditer(data, seg.offset, end - 7):
            pos = match.start()
            disp = struct.unpack_from("<i", data, pos + 3)[0]
            instruction_vaddr = seg.vaddr + pos - seg.offset
            target = instruction_vaddr + 7 + disp
            if target in target_vaddrs:
                found.setdefault(target, []).append(instruction_vaddr)
    return found


def find_arm64_references(data, segments, target_vaddrs):
    low_bits = {}
    for target in target_vaddrs:
        low_bits.setdefault(target & 0xFFF, []).append(target)
    found = {}
    for seg in (s for s in segments if s.flags & PF_X):
        end = seg.offset + seg.filesz
        for lo12, targets in low_bits.items():
            # ADD Xd, Xn, #lo12 (no shift) as little-endian bytes: b0 b1 b2 0x91
            b1_first = (lo12 & 0x3F) << 2
            pattern = re.compile(
                b"(?=.[" + re.escape(bytes([b1_first])) + b"-" + re.escape(bytes([b1_first | 3])) + b"]"
                + re.escape(bytes([lo12 >> 6])) + b"\x91)",
                re.DOTALL,
            )
            for match in pattern.finditer(data, seg.offset, end):
                pos = match.start()
                if (pos - seg.offset) % 4 != 0 or pos - 4 < seg.offset:
                    continue
                add = struct.unpack_from("<I", data, pos)[0]
                adrp = struct.unpack_from("<I", data, pos - 4)[0]
                if adrp & 0x9F000000 != 0x90000000 or adrp & 0x1F != (add >> 5) & 0x1F:
                    continue
                imm = (((adrp >> 5) & 0x7FFFF) << 2) | ((adrp >> 29) & 3)
                if imm & (1 << 20):
                    imm -= 1 << 21
                adrp_vaddr = seg.vaddr + pos - 4 - seg.offset
                target = (adrp_vaddr & ~0xFFF) + (imm << 12) + lo12
                if target in targets:
                    found.setdefault(target, []).append(adrp_vaddr)
    return found


class PcLnTab:
    def __init__(self, data, machine, segments):
        base = self._locate(data)
        (
            self.nfunc,
            _nfiles,
            header_text_start,
            funcname_off,
            _cu_off,
            _filetab_off,
            _pctab_off,
            pcln_off,
        ) = struct.unpack_from("<8Q", data, base + 8)
        if header_text_start:
            self.text_start = header_text_start
        else:
            pclntab_vaddr = offset_to_vaddr(segments, base)
            self.text_start = find_moduledata_text(data, machine, pclntab_vaddr, pclntab_vaddr + funcname_off)
        if not any(s.flags & PF_X and s.contains_vaddr(self.text_start) for s in segments):
            raise PatchError(f"runtime.text {self.text_start:#x} is not in an executable segment")
        self.data = data
        self.funcname_base = base + funcname_off
        self.ftab_base = base + pcln_off

    @staticmethod
    def _locate(data):
        for match in re.finditer(rb"\xf1\xff\xff\xff\x00\x00[\x01\x02\x04]\x08", data):
            base = match.start()
            nfunc = struct.unpack_from("<Q", data, base + 8)[0]
            if 1000 < nfunc < 10_000_000:
                return base
        raise PatchError("Go pclntab not found")

    def function_at(self, vaddr):
        target = vaddr - self.text_start
        lo, hi = 0, self.nfunc - 1
        while lo < hi:
            mid = (lo + hi + 1) // 2
            entry = struct.unpack_from("<I", self.data, self.ftab_base + mid * 8)[0]
            if entry <= target:
                lo = mid
            else:
                hi = mid - 1
        entry, func_off = struct.unpack_from("<II", self.data, self.ftab_base + lo * 8)
        name_off = struct.unpack_from("<I", self.data, self.ftab_base + func_off + 4)[0]
        start = self.funcname_base + name_off
        name = self.data[start : self.data.index(b"\0", start)].decode(errors="replace")
        return name, self.text_start + entry


def has_go_prologue(data, machine, entry_offset):
    if machine == EM_X86_64:
        return X86_GO_PROLOGUE.match(data, entry_offset, entry_offset + 13) is not None
    return data[entry_offset : entry_offset + 4] == ARM64_GO_PROLOGUE


def locate_function(data, machine, segments):
    string_offsets = find_notice_strings(data)
    if not string_offsets:
        raise PatchError("notice strings not found")
    targets = {offset_to_vaddr(segments, o): o for o in string_offsets}

    if machine == EM_X86_64:
        references = find_x86_references(data, segments, targets)
    elif machine == EM_AARCH64:
        references = find_arm64_references(data, segments, targets)
    else:
        raise PatchError(f"unsupported ELF machine {machine:#x}")

    missing = [hex(t) for t in targets if t not in references]
    if missing:
        raise PatchError(f"no code references for notice strings at {missing}")

    pclntab = PcLnTab(data, machine, segments)
    functions = {pclntab.function_at(ref) for refs in references.values() for ref in refs}
    if len(functions) != 1:
        raise PatchError(f"notice strings are referenced from {len(functions)} functions: {sorted(functions)}")
    name, entry_vaddr = next(iter(functions))
    if not name.endswith(EXPECTED_FUNCTION_SUFFIX):
        raise PatchError(f"unexpected function {name!r}, expected *{EXPECTED_FUNCTION_SUFFIX}")
    return name, entry_vaddr


def main():
    if len(sys.argv) != 2:
        print(f"usage: {sys.argv[0]} <agy-binary>", file=sys.stderr)
        return 2

    path = sys.argv[1]
    with open(path, "rb") as stream:
        data = stream.read()

    machine, segments = parse_elf(data)
    name, entry_vaddr = locate_function(data, machine, segments)
    entry_offset = vaddr_to_offset(segments, entry_vaddr)
    ret = X86_RET if machine == EM_X86_64 else ARM64_RET

    if data[entry_offset : entry_offset + len(ret)] == ret:
        print(f"already patched: {name} @ {entry_vaddr:#x}")
        return 0

    if not has_go_prologue(data, machine, entry_offset):
        raise PatchError(
            f"{name} @ {entry_vaddr:#x} does not start with a Go prologue "
            f"(bytes {data[entry_offset : entry_offset + 16].hex()}); runtime.text is likely wrong"
        )

    with open(path, "r+b") as stream:
        stream.seek(entry_offset)
        stream.write(ret)
    print(f"patched {name} @ {entry_vaddr:#x} (file offset {entry_offset:#x}) -> ret")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except PatchError as error:
        print(f"patch_agy_recovery_notice: {error}", file=sys.stderr)
        sys.exit(1)
