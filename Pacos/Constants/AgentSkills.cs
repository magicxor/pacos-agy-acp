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
            Description = "Построение графиков и диаграмм в синтаксисе Chart.js через MCP-сервер quickchart. Используй, когда пользователь просит построить график, диаграмму, чарт, воронку или визуализировать данные, а также когда на графике нужны пометки: вертикальные отсечки, выделенные периоды, стрелки и выноски с подписями.",
            Body = """
                   # ГРАФИКИ И ДИАГРАММЫ (MCP quickchart)
                   - Чтобы построить график или диаграмму (bar, line, pie, doughnut, radar, scatter и т.д.), используй MCP-инструмент create_chart сервера quickchart.
                   - Параметр chart — конфигурация Chart.js 4, переданная строкой (обычный JSON или JS-синтаксис с функциями). Используй ТОЛЬКО синтаксис Chart.js 4 (options.scales.x/y, options.plugins.title/legend); синтаксис Chart.js 2 (scales.xAxes, type 'horizontalBar') не поддерживается — для горизонтальных bar используй options.indexAxis: 'y'. Опциональные параметры: width, height, backgroundColor, format (png по умолчанию; svg, pdf).
                   - Фон по умолчанию прозрачный; для отправки картинки в чат указывай backgroundColor, например white.
                   - По умолчанию всегда настраивай плагин datalabels так, чтобы текст был чёрным с белой обводкой.
                   - Функции в конфиге (formatter/display у datalabels, ticks.callback, колбэки tooltip, цвет по условию) можно передавать двумя способами: незакавыченным JS-кодом или строкой с исходником прямо в JSON ("formatter": "function(v) { return v.y; }") — сервер скомпилирует и то, и другое, а если строка не парсится, вернёт ошибку 400 с именем опции. Но сначала проверь, нужна ли функция вообще: дефолтные подписи уже сами правильно печатают точки {x, y} (значение по оси значений), пузыри {x, y, r} (радиус) и строки гео-данных (название региона и значение). Форматер нужен только чтобы изменить этот текст, а не чтобы он вообще появился.
                   - Результат сохраняется в директорию из параметра outputDirectory: указывай выходную директорию (пути бери из меток [SYSTEM: ...]).
                   - Если инструмент вернул ошибку, покажи пользователю её текст.
                   - Карты (гео: choropleth, bubbleMap) — отдельный навык geo-maps (.agents/skills/geo-maps/SKILL.md). ОБЯЗАТЕЛЬНО прочитай его перед тем, как строить карту: без его правил карта отрисуется сломанной.
                   - Делай графики максимально наглядными:
                     1. Тип — по смыслу данных, например: сравнение категорий — bar; динамика во времени — line; доли целого — pie/doughnut (до ~6 долей); категории с подкатегориями — двухуровневый doughnut (два dataset); поэтапная воронка — funnel; зависимость двух величин — scatter. Карту (choropleth) бери, только когда важна географическая картина распределения по многим странам/регионам («где больше, где меньше»); сравнение показателя у нескольких конкретных стран — это обычный bar, а не карта.
                     2. Показывай значения прямо на графике плагином datalabels (options.plugins.datalabels; для bar: anchor 'end', align 'top'), а не только на оси.
                     3. Цвета — осмысленные для данных: цвета флагов для стран, розовый/голубой для девочек/мальчиков, зелёный/красный для роста/падения; иначе — контрастная палитра, а не один цвет на всё.
                     4. Обязателен заголовок, крупные шрифты (заголовок ~20, подписи ~14), подписи осей; легенду убирай, если она дублирует подписи.
                     5. НИКОГДА не используй \n и другие escape-последовательности в заголовках и подписях — коды символов не поддерживаются, и текст выведется буквально (косая черта и буква n). Для переноса строки передавай текст массивом строк, например title.text: ['Первая строка', 'Вторая строка']; свой formatter у datalabels для многострочной подписи тоже должен возвращать массив строк.

                   ## Пометки на графике (плагин annotation)
                   Когда на графике нужно отметить событие, период или конкретную точку — «здесь начался кризис», «это время карантина», «вот рекорд» — используй options.plugins.annotation.annotations. Это объект: ключ — произвольный id пометки, значение — сама пометка со своим type. Типы: line, box, point, label, ellipse, polygon, doughnutLabel (текст в центре бублика).
                   Координаты задаются В ЕДИНИЦАХ ДАННЫХ, а не в пикселях: xMin/xMax/yMin/yMax у line и box, xValue/yValue у point и label. На категориальной оси это название категории ("1929") или её индекс, на линейной — число, на оси времени — дата в том же формате, что и в данных.
                   Четыре рецепта, которых хватает почти всегда:
                   - Вертикальная отсечка через весь график: { "type": "line", "xMin": "1929", "xMax": "1929", "borderColor": "#c0392b", "borderWidth": 2, "borderDash": [6, 4], "label": { "display": true, "content": "Событие", "position": "start", "backgroundColor": "#c0392b", "color": "white" } }. Одинаковые xMin и xMax дают вертикаль; одинаковые yMin и yMax — горизонтальный порог (план, средняя, рекорд).
                   - Выделенный период (зона): { "type": "box", "xMin": "1929", "xMax": "1933", "backgroundColor": "rgba(192,57,43,0.1)", "borderColor": "rgba(192,57,43,0.35)", "borderWidth": 1, "label": { "display": true, "content": "Кризис", "position": { "x": "center", "y": "start" } } }. Заливка обязательно полупрозрачная, иначе она закроет собой линию графика.
                   - Стрелка с пояснением: та же line между двумя произвольными точками плюс "arrowHeads": { "end": { "display": true, "fill": true, "length": 12, "width": 8 } }. Остриё рисуется на конце (xMax, yMax) — туда пиши точку графика, на которую показываешь, а в (xMin, yMin) — место, где висит подпись.
                   - Выноска к точке: маркер { "type": "point", "xValue": "1933", "yValue": 99, "radius": 6, "backgroundColor": "..." } плюс отдельная плашка { "type": "label", "xValue": "1933", "yValue": 99, "yAdjust": -34, "content": [...], "callout": { "display": true, "borderWidth": 2 } } — callout дорисует ножку от плашки к точке, а yAdjust отведёт плашку от неё.
                   Правила:
                   - Многострочный текст пометки — массив строк: "content": ["Дно рынка", "-89% от пика"]. \n не работает и здесь (см. пункт 5 выше).
                   - Пометки не расталкиваются ни между собой, ни с подписями datalabels. Если плашки налезают друг на друга, разводи их вручную: position ("start", "center", "end" или { "x": ..., "y": ... }) плюс сдвиги в пикселях xAdjust и yAdjust. На графике с пометками datalabels обычно лучше выключить совсем: options.plugins.datalabels.display = false.
                   - 1-4 пометки на график, не больше: график не стенгазета, остальные пояснения давай текстом в ответе.
                   - Если по оси X годы, делай ось категориальной (labels: ["1925", "1926", ...], данные простым массивом): на линейной оси Chart.js печатает годы с разделителем тысяч — «1,925» вместо «1925».

                   ## Пример графика с пометками
                   Проверенный рабочий конфиг: зона, вертикальная отсечка, стрелка и выноска на одной картинке. Строгий JSON, ни одной функции.
                   ```
                   {
                     "type": "line",
                     "data": {
                       "labels": ["1925", "1926", "1927", "1928", "1929", "1930", "1931", "1932", "1933", "1934", "1935", "1936"],
                       "datasets": [
                         {
                           "label": "Dow Jones",
                           "data": [137, 157, 200, 300, 248, 164, 77, 60, 99, 104, 144, 180],
                           "borderColor": "#1f4e79",
                           "borderWidth": 3,
                           "pointRadius": 0,
                           "tension": 0.25
                         }
                       ]
                     },
                     "options": {
                       "layout": { "padding": { "top": 10, "right": 30, "bottom": 10, "left": 10 } },
                       "scales": {
                         "y": {
                           "beginAtZero": true,
                           "title": { "display": true, "text": "Индекс, пунктов", "font": { "size": 14 } }
                         }
                       },
                       "plugins": {
                         "title": { "display": true, "text": "Dow Jones, 1925-1936", "font": { "size": 20 } },
                         "legend": { "display": false },
                         "datalabels": { "display": false },
                         "annotation": {
                           "annotations": {
                             "depression": {
                               "type": "box",
                               "xMin": "1929",
                               "xMax": "1933",
                               "backgroundColor": "rgba(192,57,43,0.1)",
                               "borderColor": "rgba(192,57,43,0.35)",
                               "borderWidth": 1,
                               "label": {
                                 "display": true,
                                 "content": "Великая депрессия",
                                 "position": { "x": "center", "y": "start" },
                                 "color": "#7b241c",
                                 "font": { "size": 14, "weight": "bold" }
                               }
                             },
                             "crash": {
                               "type": "line",
                               "xMin": "1929",
                               "xMax": "1929",
                               "borderColor": "#c0392b",
                               "borderWidth": 2,
                               "borderDash": [6, 4],
                               "label": {
                                 "display": true,
                                 "content": ["Чёрный вторник", "29 октября 1929"],
                                 "position": "start",
                                 "backgroundColor": "#c0392b",
                                 "color": "white",
                                 "font": { "size": 12 }
                               }
                             },
                             "bottomArrow": {
                               "type": "line",
                               "xMin": "1934",
                               "yMin": 40,
                               "xMax": "1932",
                               "yMax": 58,
                               "borderColor": "#111",
                               "borderWidth": 2,
                               "arrowHeads": { "end": { "display": true, "fill": true, "length": 12, "width": 8 } },
                               "label": {
                                 "display": true,
                                 "content": ["Дно рынка: 41 пункт", "-89% от пика"],
                                 "position": "start",
                                 "backgroundColor": "rgba(17,17,17,0.85)",
                                 "color": "white",
                                 "font": { "size": 12 },
                                 "xAdjust": 55
                               }
                             },
                             "newDealPoint": {
                               "type": "point",
                               "xValue": "1933",
                               "yValue": 99,
                               "radius": 6,
                               "backgroundColor": "rgba(39,174,96,0.9)",
                               "borderColor": "white",
                               "borderWidth": 2
                             },
                             "newDealLabel": {
                               "type": "label",
                               "xValue": "1933",
                               "yValue": 99,
                               "yAdjust": -34,
                               "content": ["Новый курс", "Рузвельта"],
                               "backgroundColor": "rgba(39,174,96,0.9)",
                               "color": "white",
                               "font": { "size": 12 },
                               "padding": 5,
                               "callout": { "display": true, "borderColor": "#27ae60", "borderWidth": 2, "margin": 4 }
                             }
                           }
                         }
                       }
                     }
                   }
                   ```
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
                   - Сервер содержит встроенные карты — ссылайся на них ПО ИМЕНИ, НЕ вставляй GeoJSON в конфиг. Инструмент list_maps выдаёт список доступных карт, а list_maps с mapName — фичи карты (имена/id), её bbox, центроид и подобранную проекцию.
                   - Заливка по значению (type: 'choropleth'): dataset { map: 'world' | 'us-states', data: [{ feature: 'Germany', value: 83 }, ...] }; фичи матчатся по имени или id без учёта регистра. Одна страна с её регионами — map: ISO-3 код страны ('deu', 'fra', ...).
                   - Точки/пузыри на карте (type: 'bubbleMap'): dataset { outline: 'world', data: [{ longitude, latitude, value }] }.
                   - Проекцию (options.scales.projection.projection) задавать НЕ НУЖНО — сервер сам наводит её на карту. Это работает и для отдельных стран, включая Россию, США, Новую Зеландию и Фиджи. Если нужно показать не всю карту, а её часть — см. раздел 6 «Приближение к региону». Всё остальное — подложку, границы, цветовую шкалу и легенду — задавай явно по правилам ниже.

                   ## 1. Фон и невидимые страны
                   Страны, которых нет в массиве data, физически не отрисовываются (остаются прозрачными). Чтобы они имели дефолтный цвет (например, серый), ОБЯЗАТЕЛЬНО задавай в настройках dataset свойство outlineBackgroundColor: "#e0e0e0" (светло-серый фон подложки всей карты). Без этого пустые страны сольются с цветом холста или покажутся голубыми из-за дефолтных настроек.
                   Тем же цветом задавай scales.color.missing: "#e0e0e0" — это заливка для фич, которые в data есть, но без значения (по умолчанию прозрачная). Это отдельное свойство: outlineBackgroundColor красит подложку, missing — фичи без данных.

                   ## 2. Границы стран и материка
                   Чтобы страны не сливались в одно пятно, всегда задавай в dataset: borderColor: 'rgba(0,0,0,0.3)' и borderWidth: 1. Для красивой внешней границы всей карты используй outlineBorderColor: '#111' и outlineBorderWidth: 1.5.

                   ## 3. Цветовая шкала и данные (ВАЖНО!)
                   НИКОГДА не используй backgroundColor внутри объекта dataset — это сломает логику раскраски. Цвета задаются исключительно через объект шкалы options.scales.color.
                   - Используй строковые названия палитр D3, например: interpolate: "Oranges", "Greens", "Blues". НЕ передавай туда JS-функции.
                   - Обязательно указывай axis: "x" в scales.color, иначе шкала выкинет ошибку.
                   - Указывай display: true, чтобы шкала и её легенда точно отрисовались.

                   ## 4. Настройка легенды
                   По умолчанию текст на цветовой шкале может быть бледным или обрезаться.
                   - Чтобы текст был чётким, добавляй ticks: { "color": "black", "font": { "size": 14 } } внутрь scales.color.
                   - Шкалу ставь вертикально у ПРАВОГО края: legend: { "position": "bottom-right", "align": "right", "length": 220, "width": 36 }. Тогда подписи уходят в поле справа от карты. НЕ используй "bottom-left" с align "right": карта теперь заполняет весь кадр, и числа лягут поверх стран — их будет не прочитать.
                   - Обязательно оставляй поле справа под подписи, иначе они обрежутся: options.layout.padding = { "left": 20, "right": 70, "top": 10, "bottom": 20 }. Если значения длиннее 4 знаков — увеличивай right.

                   ## 5. Подписи регионов
                   Подписи на карте рисует тот же плагин datalabels, и для карт он по умолчанию выключен. Включай его (достаточно задать любую его опцию, например color), когда фич немного и подписи не превратятся в кашу: области или воеводства одной страны, штаты США, десяток стран. На карте мира и на us-counties оставляй выключенным — значения там читаются по цветовой шкале: options.plugins.datalabels.display = false.
                   Включённый datalabels сам подписывает регион двумя строками: сверху название, снизу значение. Форматер для этого НЕ НУЖЕН.
                   Названия фич во встроенных картах английские (Brest, Gomel, City of Minsk). Чтобы подписи были русскими, добавляй в строку данных поле label — на карту попадёт именно оно: { "feature": "Minsk", "label": "Минская", "value": 1471 }. НЕ собирай для этого отдельный массив названий и не привязывай его через context.dataIndex — поле label лежит рядом со значением, и перепутать порядок невозможно.
                   Если всё же пишешь свой formatter: название фичи лежит в value.feature.properties.name (в value.feature.name его НЕТ, там undefined), значение — в value.value, а для двух строк возвращай массив строк.
                   Обычную легенду датасета убирай всегда: options.plugins.legend.display = false — цветовая шкала её заменяет.

                   ## 6. Приближение к региону (кадрирование)
                   Если нужна не вся карта, а её часть — «только Европа», «только Дальний Восток», «Скандинавия» — НЕ пытайся собрать урезанный список фич и не подбирай проекцию вручную. Задай options.scales.projection.fit (полный путь именно такой: шкала называется projection и лежит внутри options.scales) — он задаёт, на что смотрит камера, независимо от того, что нарисовано. Всё, что не попало в кадр, обрезается само.
                   Три формы fit (выбирай любую):
                   - по списку фич той же карты: "fit": { "map": "rus", "features": ["Amur", "Khabarovsk", "Sakhalin"] } — самый надёжный способ, имена бери из list_maps;
                   - по целой карте: "fit": { "map": "deu" } — например, показать Германию на карте мира вместе с соседями;
                   - по прямоугольнику координат: "fit": [запад, юг, восток, север] в градусах, например [-25, 34, 45, 72] для Европы. Для региона за 180-м меридианом запад может быть больше востока: [160, 62, -172, 72] — это Чукотка.
                   Проекцию при этом НЕ указывай: сервер наведёт её именно на регион из fit (в том числе повернёт глобус для регионов у 180-го меридиана). Если укажешь проекцию сам — наводить её придётся тоже самому, и регион у 180-го меридиана разрежется пополам.
                   Пример: карта мира, приближенная к Европе — к конфигу ниже достаточно добавить в options.scales.projection значение { "axis": "x", "fit": [-25, 34, 45, 72] }.

                   ## Пример карты мира со всеми правилами
                   Проверенный рабочий конфиг — бери его за основу и меняй только label, title и data. Передавай строгим JSON (двойные кавычки), проекцию не добавляй. Стран много, поэтому подписи здесь выключены (см. раздел 5).
                   ```
                   {
                     "type": "choropleth",
                     "data": {
                       "datasets": [
                         {
                           "label": "Экспорт титана",
                           "map": "world",
                           "borderColor": "rgba(0,0,0,0.3)",
                           "borderWidth": 1,
                           "outlineBorderColor": "#111",
                           "outlineBorderWidth": 1.5,
                           "outlineBackgroundColor": "#e0e0e0",
                           "data": [
                             { "feature": "Mozambique", "value": 950 },
                             { "feature": "South Africa", "value": 780 },
                             { "feature": "China", "value": 900 },
                             { "feature": "Germany", "value": 100 }
                           ]
                         }
                       ]
                     },
                     "options": {
                       "layout": {
                         "padding": { "left": 20, "right": 70, "top": 10, "bottom": 20 }
                       },
                       "plugins": {
                         "title": {
                           "display": true,
                           "text": "Топ экспортеров титана (условные единицы / млн $)",
                           "font": { "size": 20 }
                         },
                         "legend": { "display": false },
                         "datalabels": { "display": false }
                       },
                       "scales": {
                         "color": {
                           "axis": "x",
                           "display": true,
                           "missing": "#e0e0e0",
                           "interpolate": "Oranges",
                           "ticks": {
                             "color": "black",
                             "font": { "size": 14 }
                           },
                           "legend": {
                             "position": "bottom-right",
                             "align": "right",
                             "length": 220,
                             "width": 36
                           }
                         }
                       }
                     }
                   }
                   ```

                   ## Пример карты страны с подписанными регионами
                   Областей немного, поэтому подписи включены; русские названия берутся из label в строках данных, форматера нет. Проекцию снова не задаём — сервер сам наведёт её на страну.
                   ```
                   {
                     "type": "choropleth",
                     "data": {
                       "datasets": [
                         {
                           "label": "Население",
                           "map": "blr",
                           "borderColor": "rgba(0,0,0,0.3)",
                           "borderWidth": 1,
                           "outlineBorderColor": "#111",
                           "outlineBorderWidth": 1.5,
                           "outlineBackgroundColor": "#e0e0e0",
                           "data": [
                             { "feature": "Brest", "label": "Брестская", "value": 1348 },
                             { "feature": "Vitebsk", "label": "Витебская", "value": 1135 },
                             { "feature": "Gomel", "label": "Гомельская", "value": 1388 },
                             { "feature": "Grodno", "label": "Гродненская", "value": 1026 },
                             { "feature": "Minsk", "label": "Минская", "value": 1471 },
                             { "feature": "Mogilev", "label": "Могилёвская", "value": 1020 },
                             { "feature": "City of Minsk", "label": "Минск", "value": 1996 }
                           ]
                         }
                       ]
                     },
                     "options": {
                       "layout": {
                         "padding": { "left": 20, "right": 70, "top": 10, "bottom": 20 }
                       },
                       "plugins": {
                         "title": {
                           "display": true,
                           "text": "Население областей Беларуси, тыс. чел.",
                           "font": { "size": 20 }
                         },
                         "legend": { "display": false },
                         "datalabels": {
                           "color": "black",
                           "font": { "size": 14 },
                           "textStrokeColor": "white",
                           "textStrokeWidth": 3
                         }
                       },
                       "scales": {
                         "color": {
                           "axis": "x",
                           "display": true,
                           "missing": "#e0e0e0",
                           "interpolate": "Blues",
                           "ticks": {
                             "color": "black",
                             "font": { "size": 14 }
                           },
                           "legend": {
                             "position": "bottom-right",
                             "align": "right",
                             "length": 220,
                             "width": 36
                           }
                         }
                       }
                     }
                   }
                   ```
                   """,
        },
    ];
}
