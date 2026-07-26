using Pacos.Models;

namespace Pacos.Constants;

/// <summary>
/// The agy Agent Skills written into every chat workspace under
/// <c>.agents/skills/&lt;FolderName&gt;/SKILL.md</c> by
/// <see cref="Services.GenerativeAi.ChatWorkspaceProvisioner"/>. Each entry holds tool
/// instructions that used to live in the steering file and were therefore prepended to
/// every prompt; as skills they are loaded on demand instead.
/// </summary>
public static class AgentSkills
{
    public static readonly IReadOnlyList<AgentSkill> All =
    [
        new AgentSkill
        {
            FolderName = "gallery-download",
            Title = "картинки и обои с сайтов-галерей",
            Description = "Поиск и скачивание картинок или обоев с сайтов-галерей (e621, wallhaven и другие) по тегу или запросу через MCP-сервер gallerydl. Используй, когда пользователь просит найти, скачать или показать картинки, обои или арт с какого-то сайта.",
            Body = """
                   # СКАЧИВАНИЕ КАРТИНОК ИЗ ГАЛЕРЕЙ (MCP)
                   - Тебе доступны MCP-инструменты сервера gallerydl: list_resources (список поддерживаемых сайтов) и download_gallery (скачивание картинок по тегу/запросу с сайта-галереи).
                   - Когда пользователь просит найти/скачать/показать картинки или обои по тегу или запросу с какого-то сайта, используй download_gallery.
                   - В параметр path передавай АБСОЛЮТНЫЙ путь текущей выходной директории (из метки "[SYSTEM: Выходная директория для файлов: <путь>]"). Файлы из неё отправятся в чат автоматически — перемещать их через move_file НЕ нужно.
                   - Параметры download_gallery: resource — id сайта (например, e621.net-safe, wallhaven.cc-safe; полный список даст list_resources), query — тег или поисковый запрос, take — сколько картинок скачать (если пользователь не уточнил, бери 1-3), skip — сколько первых результатов пропустить.
                   - Если инструмент вернул ошибку, покажи пользователю её текст.
                   """,
        },
        new AgentSkill
        {
            FolderName = "web-crawling",
            Title = "веб-страницы, скриншоты, PDF",
            Description = "Чтение веб-страниц, их скриншоты и сохранение в PDF через MCP-сервер crawl4ai. Используй, когда нужно открыть URL, получить содержимое страницы в Markdown или HTML, сделать скриншот страницы или сохранить её в PDF.",
            Body = """
                   # ВЕБ-СТРАНИЦЫ, СКРИНШОТЫ, PDF (MCP crawl4ai)
                   - Чтобы прочитать веб-страницу, сделать её скриншот или PDF, используй MCP-инструменты сервера crawl4ai: md (страница в Markdown), html, screenshot, pdf, crawl (несколько URL сразу), ask.
                   - Результат сохраняется в директорию из параметра outputDirectory: если файл нужно отдать пользователю — указывай выходную директорию, если он нужен только тебе для ответа — временную (пути бери из меток [SYSTEM: ...]).
                   """,
        },
        new AgentSkill
        {
            FolderName = "chart-generation",
            Title = "графики и диаграммы",
            Description = "Построение графиков и диаграмм в синтаксисе Chart.js через MCP-сервер quickchart. Используй, когда пользователь просит построить график, диаграмму, чарт, воронку или визуализировать данные.",
            Body = """
                   # ГРАФИКИ И ДИАГРАММЫ (MCP quickchart)
                   - Чтобы построить график или диаграмму (bar, line, pie, doughnut, radar, scatter и т.д.), используй MCP-инструмент create_chart сервера quickchart.
                   - Параметр chart — конфигурация Chart.js 4, переданная строкой (обычный JSON или JS-синтаксис с функциями). Используй ТОЛЬКО синтаксис Chart.js 4 (options.scales.x/y, options.plugins.title/legend); синтаксис Chart.js 2 (scales.xAxes, type 'horizontalBar') не поддерживается — для горизонтальных bar используй options.indexAxis: 'y'. Опциональные параметры: width, height, backgroundColor, format (png по умолчанию; svg, pdf).
                   - Фон по умолчанию прозрачный; для отправки картинки в чат указывай backgroundColor, например white.
                   - По умолчанию всегда настраивай плагин datalabels так, чтобы текст был чёрным с белой обводкой.
                   - Результат сохраняется в директорию из параметра outputDirectory: указывай выходную директорию (пути бери из меток [SYSTEM: ...]).
                   - Если инструмент вернул ошибку, покажи пользователю её текст.
                   - Карты (гео: choropleth, bubbleMap) — отдельный навык geo-maps (.agents/skills/geo-maps/SKILL.md). ОБЯЗАТЕЛЬНО прочитай его перед тем, как строить карту: без его правил карта отрисуется сломанной.
                   - Делай графики максимально наглядными:
                     1. Тип — по смыслу данных, например: сравнение категорий — bar; динамика во времени — line; доли целого — pie/doughnut (до ~6 долей); категории с подкатегориями — двухуровневый doughnut (два dataset); поэтапная воронка — funnel; зависимость двух величин — scatter. Карту (choropleth) бери, только когда важна географическая картина распределения по многим странам/регионам («где больше, где меньше»); сравнение показателя у нескольких конкретных стран — это обычный bar, а не карта.
                     2. Показывай значения прямо на графике плагином datalabels (options.plugins.datalabels; для bar: anchor 'end', align 'top'), а не только на оси.
                     3. Цвета — осмысленные для данных: цвета флагов для стран, розовый/голубой для девочек/мальчиков, зелёный/красный для роста/падения; иначе — контрастная палитра, а не один цвет на всё.
                     4. Обязателен заголовок, крупные шрифты (заголовок ~20, подписи ~14), подписи осей; легенду убирай, если она дублирует подписи.
                     5. НИКОГДА не используй \n и другие escape-последовательности в заголовках и подписях — коды символов не поддерживаются, и текст выведется буквально (косая черта и буква n). Для переноса строки передавай текст массивом строк, например title.text: ['Первая строка', 'Вторая строка'].
                   """,
        },
        new AgentSkill
        {
            FolderName = "geo-maps",
            Title = "карты: choropleth и bubbleMap",
            Description = "Построение гео-карт (choropleth — заливка стран и регионов по значению, bubbleMap — точки на карте) через MCP-сервер quickchart и chartjs-chart-geo. Используй, когда данные нужно показать на карте мира, отдельной страны, её регионов или штатов США.",
            Body = """
                   # ГЕО-КАРТЫ (choropleth и bubbleMap, MCP quickchart)
                   - Этот навык дополняет chart-generation (.agents/skills/chart-generation/SKILL.md): общие правила create_chart — параметр chart, backgroundColor, format, outputDirectory, заголовки и шрифты — бери оттуда.
                   - Карту бери, только когда важна географическая картина распределения по многим странам/регионам («где больше, где меньше»). Сравнение показателя у нескольких конкретных стран — это обычный bar, а не карта.
                   - Сервер содержит встроенные карты — ссылайся на них ПО ИМЕНИ, НЕ вставляй GeoJSON в конфиг. Инструмент list_maps выдаёт список доступных карт, а list_maps с mapName — список фич карты (имена/id) на случай, если не уверен в написании.
                   - Заливка по значению (type: 'choropleth'): dataset { map: 'world' | 'us-states', data: [{ feature: 'Germany', value: 83 }, ...] }; фичи матчатся по имени или id без учёта регистра. Одна страна с её регионами — map: ISO-3 код страны ('deu', 'fra', ...).
                   - Точки/пузыри на карте (type: 'bubbleMap'): dataset { outline: 'world', data: [{ longitude, latitude, value }] }.
                   - Проекцию (scales.projection) настраивать не нужно — сервер подставит её сам. Всё остальное — подложку, границы, цветовую шкалу и легенду — задавай явно по правилам ниже.

                   ## 1. Фон и невидимые страны
                   Страны, которых нет в массиве data, физически не отрисовываются (остаются прозрачными). Чтобы они имели дефолтный цвет (например, серый), ОБЯЗАТЕЛЬНО задавай в настройках dataset свойство outlineBackgroundColor: '#e0e0e0' (светло-серый фон подложки всей карты). Без этого пустые страны сольются с цветом холста или покажутся голубыми из-за дефолтных настроек.

                   ## 2. Границы стран и материка
                   Чтобы страны не сливались в одно пятно, всегда задавай в dataset: borderColor: 'rgba(0,0,0,0.3)' и borderWidth: 1. Для красивой внешней границы всей карты используй outlineBorderColor: '#111' и outlineBorderWidth: 1.5.

                   ## 3. Цветовая шкала и данные (ВАЖНО!)
                   НИКОГДА не используй backgroundColor внутри объекта dataset — это сломает логику раскраски. Цвета задаются исключительно через объект шкалы options.scales.color.
                   - Используй строковые названия палитр D3, например: interpolate: 'Oranges', 'Greens', 'Blues'. НЕ передавай туда JS-функции.
                   - Обязательно указывай axis: 'x' в scales.color, иначе шкала выкинет ошибку.

                   ## 4. Настройка легенды
                   По умолчанию текст на цветовой шкале может быть бледным или обрезаться.
                   - Чтобы текст был чётким, добавляй ticks: { color: 'black', font: { size: 14 } } внутрь scales.color.
                   - Чтобы шкала не наезжала на страны (типа Новой Зеландии) и не обрезалась, используй вертикальное отображение слева: legend: { position: 'bottom-left', align: 'right', length: 200, width: 40 }.
                   - Обязательно добавляй отступы по краям самого графика, чтобы легенда влезла, например: options.layout.padding = { left: 40, right: 40, bottom: 40 }.

                   ## 5. Подписи значений
                   Значения на карте читаются по цветовой шкале, поэтому плагин datalabels здесь только мешает (он подпишет каждую фичу). Отключай его: options.plugins.datalabels.display = false. Обычную легенду датасета тоже убирай: options.plugins.legend.display = false — цветовая шкала её заменяет.

                   ## Пример choropleth со всеми правилами
                   ```
                   {
                     type: 'choropleth',
                     data: {
                       datasets: [{
                         label: 'Население, млн',
                         map: 'world',
                         data: [{ feature: 'Germany', value: 83 }, { feature: 'France', value: 68 }],
                         outlineBackgroundColor: '#e0e0e0',
                         outlineBorderColor: '#111',
                         outlineBorderWidth: 1.5,
                         borderColor: 'rgba(0,0,0,0.3)',
                         borderWidth: 1
                       }]
                     },
                     options: {
                       layout: { padding: { left: 40, right: 40, bottom: 40 } },
                       plugins: {
                         title: { display: true, text: 'Население стран Европы', font: { size: 20 } },
                         legend: { display: false },
                         datalabels: { display: false }
                       },
                       scales: {
                         color: {
                           axis: 'x',
                           interpolate: 'Oranges',
                           ticks: { color: 'black', font: { size: 14 } },
                           legend: { position: 'bottom-left', align: 'right', length: 200, width: 40 }
                         }
                       }
                     }
                   }
                   ```
                   """,
        },
    ];
}
