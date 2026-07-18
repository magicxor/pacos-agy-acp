use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::{HashMap, HashSet};
use std::sync::atomic::AtomicBool;
use std::sync::Arc;

#[derive(Debug, Deserialize)]
pub struct JsonRpcRequest {
    pub id: Option<Value>,
    pub method: Option<String>,
    pub params: Option<Value>,
}

#[derive(Debug, Serialize)]
pub struct JsonRpcResponse {
    pub jsonrpc: &'static str,
    pub id: Value,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub result: Option<Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<Value>,
}

#[derive(Debug, Serialize)]
pub struct JsonRpcNotification {
    pub jsonrpc: &'static str,
    pub method: String,
    pub params: Value,
}

/// Persisted session→conversation mapping stored in ~/.openab/agy-acp/sessions.json
#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct SessionStore {
    pub sessions: HashMap<String, StoredSession>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoredSession {
    pub conversation_id: Option<String>,
    #[serde(default)]
    pub last_step_idx: i64,
    #[serde(default)]
    pub model_id: Option<String>,
}

pub struct Session {
    pub conversation_id: Option<String>,
    pub last_step_idx: i64,
    pub model_id: Option<String>,
}

/// Tracks streaming poll state shared between the polling thread and main task.
pub struct StreamingState {
    pub conversation_id: Option<String>,
    pub base_step_idx: i64,
    pub last_step_idx: i64,
    pub emitted_len: HashMap<i64, usize>,
    pub emitted_tool_steps: HashSet<i64>,
    pub had_updates: bool,
}

/// Output from prompt execution (used to separate lock-free execution from state update).
pub struct PromptOutput {
    pub response_lines: Vec<String>,
    pub session_update: Option<(Option<String>, i64)>,
}

/// Drop guard that sets stop_polling flag when the future is dropped (task abort safety).
pub struct StopGuard(pub Arc<AtomicBool>);
impl Drop for StopGuard {
    fn drop(&mut self) {
        self.0.store(true, std::sync::atomic::Ordering::SeqCst);
    }
}
