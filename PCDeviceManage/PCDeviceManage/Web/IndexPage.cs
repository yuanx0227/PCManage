namespace PCDeviceManage
{
    internal static class IndexPage
    {
        public const string Html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"" />
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>&#x7535;&#x8111;&#x63A7;&#x5236;</title>
    <style>
        :root {
            color-scheme: light dark;
            --bg: #f4f5f7;
            --card: #ffffff;
            --text: #1f2328;
            --muted: #6b7280;
            --line: #e3e5e8;
            --accent: #2563eb;
            --danger: #c62828;
            --ok: #2e7d32;
        }

        @media (prefers-color-scheme: dark) {
            :root {
                --bg: #17181a;
                --card: #212225;
                --text: #e6e7e9;
                --muted: #9a9ea6;
                --line: #33353a;
                --accent: #5b8def;
                --danger: #ef6b6b;
                --ok: #6cc070;
            }
        }

        * { box-sizing: border-box; }

        body {
            margin: 0;
            color: var(--text);
            background: var(--bg);
            font-family: ""Segoe UI"", ""Microsoft YaHei UI"", ""PingFang SC"", system-ui, sans-serif;
            font-size: 15px;
            line-height: 1.5;
            -webkit-tap-highlight-color: transparent;
        }

        .page {
            width: min(560px, 100%);
            margin: 0 auto;
            padding: 20px 16px 32px;
        }

        header {
            display: flex;
            align-items: baseline;
            justify-content: space-between;
            gap: 12px;
            margin-bottom: 16px;
        }

        h1 {
            margin: 0;
            font-size: 20px;
            font-weight: 600;
        }

        .status {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            color: var(--muted);
            font-size: 13px;
        }

        .status::before {
            width: 6px;
            height: 6px;
            border-radius: 50%;
            background: var(--muted);
            content: """";
        }

        .status.ok::before { background: var(--ok); }
        .status.error::before { background: var(--danger); }

        section {
            margin-bottom: 12px;
            padding: 16px;
            border: 1px solid var(--line);
            border-radius: 10px;
            background: var(--card);
        }

        h2 {
            margin: 0 0 12px;
            color: var(--muted);
            font-size: 13px;
            font-weight: 600;
        }

        .section-head {
            display: flex;
            align-items: center;
            justify-content: space-between;
            margin-bottom: 12px;
        }

        .section-head h2 { margin: 0; }

        button.link {
            height: auto;
            padding: 0;
            color: var(--accent);
            border: none;
            background: none;
            font-size: 13px;
        }

        input[type=""range""]:disabled { opacity: 0.4; }

        .row + .row {
            margin-top: 14px;
            padding-top: 14px;
            border-top: 1px solid var(--line);
        }

        .row-label {
            display: flex;
            justify-content: space-between;
            gap: 8px;
            margin-bottom: 6px;
        }

        .row-label .tag {
            color: var(--muted);
            font-size: 13px;
        }

        .slider {
            display: flex;
            align-items: center;
            gap: 12px;
        }

        .slider input { flex: 1; }

        .value {
            min-width: 3em;
            color: var(--muted);
            font-variant-numeric: tabular-nums;
            text-align: right;
        }

        input[type=""range""] {
            height: 28px;
            margin: 0;
            accent-color: var(--accent);
        }

        input[type=""number""] {
            width: 100%;
            min-width: 0;
            height: 38px;
            padding: 0 10px;
            color: var(--text);
            border: 1px solid var(--line);
            border-radius: 8px;
            background: var(--card);
            font: inherit;
        }

        input[type=""number""]:focus {
            border-color: var(--accent);
            outline: none;
        }

        .empty {
            color: var(--muted);
            font-size: 14px;
        }

        .presets {
            display: grid;
            grid-template-columns: repeat(4, minmax(0, 1fr));
            gap: 8px;
            margin-bottom: 10px;
        }

        .presets button { padding: 0 4px; }

        .inline {
            display: flex;
            gap: 8px;
        }

        .inline input { flex: 1; }

        button {
            height: 38px;
            padding: 0 14px;
            color: var(--text);
            border: 1px solid var(--line);
            border-radius: 8px;
            background: var(--card);
            cursor: pointer;
            font: inherit;
            white-space: nowrap;
        }

        button:hover { border-color: var(--muted); }
        button:active { background: var(--bg); }

        button.primary {
            color: #fff;
            border-color: var(--accent);
            background: var(--accent);
        }

        button.danger { color: var(--danger); }

        .timer-state {
            display: none;
            align-items: center;
            justify-content: space-between;
            gap: 12px;
            margin-top: 12px;
            padding-top: 12px;
            border-top: 1px solid var(--line);
        }

        .timer-state.active { display: flex; }

        .timer-state strong {
            font-variant-numeric: tabular-nums;
            font-weight: 600;
        }

        .timer-state .muted {
            color: var(--muted);
            font-size: 13px;
        }

        .actions {
            display: grid;
            grid-template-columns: repeat(3, 1fr);
            gap: 8px;
        }
    </style>
</head>
<body>
    <div class=""page"">
        <header>
            <h1>&#x7535;&#x8111;&#x63A7;&#x5236;</h1>
            <span class=""status"" id=""status"">&#x8FDE;&#x63A5;&#x4E2D;</span>
        </header>

        <section>
            <div class=""section-head"">
                <h2>&#x663E;&#x793A;&#x5668;&#x4EAE;&#x5EA6;</h2>
                <button class=""link"" id=""rescanDisplays"">&#x91CD;&#x65B0;&#x68C0;&#x6D4B;</button>
            </div>
            <div id=""displayList"">
                <div class=""empty"">&#x6B63;&#x5728;&#x8BFB;&#x53D6;&#x663E;&#x793A;&#x5668;&#x4FE1;&#x606F;&#x2026;</div>
            </div>
        </section>

        <section>
            <h2>&#x97F3;&#x91CF;</h2>
            <div class=""slider"">
                <input type=""range"" id=""volumeRange"" min=""0"" max=""100"" value=""0"" aria-label=""&#x97F3;&#x91CF;"" />
                <span class=""value"" id=""volumeValue"">0%</span>
            </div>
        </section>

        <section>
            <h2>&#x5B9A;&#x65F6;&#x5173;&#x673A;</h2>
            <div class=""presets"">
                <button data-minutes=""30"">30&#x5206;&#x949F;</button>
                <button data-minutes=""60"">1&#x5C0F;&#x65F6;</button>
                <button data-minutes=""90"">1.5&#x5C0F;&#x65F6;</button>
                <button data-minutes=""120"">2&#x5C0F;&#x65F6;</button>
            </div>
            <div class=""inline"">
                <input id=""shutdownInput"" type=""number"" min=""1"" max=""10000"" inputmode=""numeric"" placeholder=""&#x81EA;&#x5B9A;&#x4E49;&#x5206;&#x949F;&#x6570;"" />
                <button class=""primary"" id=""applyShutdown"">&#x5F00;&#x59CB;</button>
            </div>
            <div class=""timer-state"" id=""timerState"">
                <div>
                    <strong id=""timerRemaining""></strong>
                    <div class=""muted"" id=""timerAt""></div>
                </div>
                <button id=""cancelShutdown"">&#x53D6;&#x6D88;</button>
            </div>
        </section>

        <section>
            <h2>&#x7CFB;&#x7EDF;</h2>
            <div class=""actions"">
                <button id=""lockWindows"">&#x9501;&#x5B9A;</button>
                <button class=""danger"" id=""shutdownNow"">&#x5173;&#x673A;</button>
                <button class=""danger"" id=""restartNow"">&#x91CD;&#x542F;</button>
            </div>
        </section>
    </div>

    <script>
        const $ = id => document.getElementById(id);
        const statusEl = $(""status"");
        const displayList = $(""displayList"");
        const volumeRange = $(""volumeRange"");
        const volumeValue = $(""volumeValue"");
        const shutdownInput = $(""shutdownInput"");
        const timerState = $(""timerState"");
        const timerRemaining = $(""timerRemaining"");
        const timerAt = $(""timerAt"");
        let shutdownSeconds = 0;
        let countdownTimer = null;

        function requestText(url, params) {
            const options = { cache: ""no-store"" };
            if (params) {
                options.method = ""POST"";
                options.headers = { ""Content-Type"": ""application/x-www-form-urlencoded"", ""X-PCM-Request"": ""1"" };
                options.body = new URLSearchParams(params);
            }
            return fetch(url, options).then(response => {
                if (response.status === 401) location.replace(""/login"");
                if (!response.ok) throw new Error(response.status);
                return response.text();
            });
        }

        function send(url, params) {
            return requestText(url, params)
                .then(text => { setStatus(""\u5DF2\u8FDE\u63A5"", ""ok""); return text; })
                .catch(error => { setStatus(""\u8FDE\u63A5\u5931\u8D25"", ""error""); throw error; });
        }

        function post(url, params) {
            return send(url, params || {});
        }

        function setStatus(text, state) {
            statusEl.textContent = text;
            statusEl.className = ""status"" + (state ? "" "" + state : """");
        }

        function throttle(fn, wait) {
            let last = 0;
            let pending = null;
            return value => {
                const now = Date.now();
                clearTimeout(pending);
                if (now - last >= wait) {
                    last = now;
                    fn(value);
                } else {
                    pending = setTimeout(() => { last = Date.now(); fn(value); }, wait - (now - last));
                }
            };
        }

        function clamp(value) {
            const parsed = parseInt(value, 10);
            if (Number.isNaN(parsed)) return 0;
            return Math.max(0, Math.min(100, parsed));
        }

        function createSlider(initial, label, onChange, disabled) {
            const wrap = document.createElement(""div"");
            wrap.className = ""slider"";
            const range = document.createElement(""input"");
            range.type = ""range"";
            range.min = ""0"";
            range.max = ""100"";
            range.value = clamp(initial);
            range.disabled = !!disabled;
            range.setAttribute(""aria-label"", label);
            const value = document.createElement(""span"");
            value.className = ""value"";
            value.textContent = initial < 0 ? ""--"" : range.value + ""%"";
            const sendThrottled = throttle(onChange, 150);
            range.addEventListener(""input"", () => {
                value.textContent = range.value + ""%"";
                sendThrottled(range.value);
            });
            wrap.appendChild(range);
            wrap.appendChild(value);
            return wrap;
        }

        function renderDisplays(items) {
            displayList.innerHTML = """";
            if (!items || items.length === 0) {
                displayList.innerHTML = ""<div class='empty'>\u672A\u68C0\u6D4B\u5230\u53EF\u8C03\u8282\u4EAE\u5EA6\u7684\u663E\u793A\u5668</div>"";
                return;
            }

            items.forEach((item, index) => {
                const name = item.name || (""\u663E\u793A\u5668 "" + (index + 1));
                const row = document.createElement(""div"");
                row.className = ""row"";

                const label = document.createElement(""div"");
                label.className = ""row-label"";
                const title = document.createElement(""span"");
                title.textContent = name;
                const tag = document.createElement(""span"");
                tag.className = ""tag"";
                tag.textContent = (item.isInternal ? ""\u5185\u7F6E"" : ""\u5916\u63A5"") + (item.supported ? """" : "" \u00B7 \u4E0D\u652F\u6301\u8C03\u8282"");
                label.appendChild(title);
                label.appendChild(tag);

                row.appendChild(label);
                row.appendChild(createSlider(item.brightness, name + "" \u4EAE\u5EA6"", value =>
                    post(""/api/displays/"" + encodeURIComponent(item.id) + ""/brightness"", { value: value }).catch(() => {}), !item.supported));
                displayList.appendChild(row);
            });
        }

        function loadDisplays(request) {
            return (request || send(""/api/displays?refresh=true"")).then(data => {
                renderDisplays(data ? JSON.parse(data) : []);
            }).catch(() => {
                displayList.innerHTML = ""<div class='empty'>\u8BFB\u53D6\u663E\u793A\u5668\u4FE1\u606F\u5931\u8D25</div>"";
            });
        }

        function rescanDisplays() {
            displayList.innerHTML = ""<div class='empty'>\u6B63\u5728\u91CD\u65B0\u68C0\u6D4B\u2026</div>"";
            return loadDisplays(post(""/api/displays/rescan""));
        }

        function loadVolume() {
            return send(""/Home/GetVolume"").then(data => {
                volumeRange.value = clamp(data);
                volumeValue.textContent = volumeRange.value + ""%"";
            }).catch(() => {});
        }

        const sendVolume = throttle(value => post(""/Home/SetVolume"", { volume: value }).catch(() => {}), 150);

        function formatDuration(totalSeconds) {
            const h = Math.floor(totalSeconds / 3600);
            const m = Math.floor((totalSeconds % 3600) / 60);
            const s = totalSeconds % 60;
            const pad = n => String(n).padStart(2, ""0"");
            return (h > 0 ? h + "":"" + pad(m) : m) + "":"" + pad(s);
        }

        function formatClock(date) {
            return String(date.getHours()).padStart(2, ""0"") + "":"" + String(date.getMinutes()).padStart(2, ""0"");
        }

        function updateCountdown() {
            if (shutdownSeconds <= 0) {
                timerState.classList.remove(""active"");
                return;
            }
            timerState.classList.add(""active"");
            timerRemaining.textContent = ""\u5269\u4F59 "" + formatDuration(shutdownSeconds);
            timerAt.textContent = ""\u5C06\u4E8E "" + formatClock(new Date(Date.now() + shutdownSeconds * 1000)) + "" \u5173\u673A"";
        }

        function startCountdown(seconds) {
            shutdownSeconds = seconds;
            if (countdownTimer) clearInterval(countdownTimer);
            countdownTimer = null;
            updateCountdown();
            if (shutdownSeconds <= 0) return;
            countdownTimer = setInterval(() => {
                shutdownSeconds -= 1;
                updateCountdown();
                if (shutdownSeconds <= 0) {
                    clearInterval(countdownTimer);
                    countdownTimer = null;
                }
            }, 1000);
        }

        function applyShutdown(minutes) {
            const normalized = Math.max(0, parseInt(minutes || 0, 10) || 0);
            const seconds = normalized * 60;
            post(""/Home/TimingShutdown"", { seconds: seconds })
                .then(() => startCountdown(seconds))
                .catch(() => {});
        }

        function confirmed(message, url) {
            if (window.confirm(message)) post(url).catch(() => {});
        }

        volumeRange.addEventListener(""input"", () => {
            volumeValue.textContent = volumeRange.value + ""%"";
            sendVolume(volumeRange.value);
        });

        document.querySelectorAll(""[data-minutes]"").forEach(button => {
            button.addEventListener(""click"", () => applyShutdown(button.dataset.minutes));
        });
        $(""applyShutdown"").addEventListener(""click"", () => {
            const minutes = parseInt(shutdownInput.value, 10);
            if (!minutes || minutes <= 0) {
                shutdownInput.focus();
                return;
            }
            applyShutdown(minutes);
            shutdownInput.value = """";
        });
        shutdownInput.addEventListener(""keydown"", event => {
            if (event.key === ""Enter"") $(""applyShutdown"").click();
        });
        $(""cancelShutdown"").addEventListener(""click"", () => applyShutdown(0));
        $(""rescanDisplays"").addEventListener(""click"", rescanDisplays);
        $(""lockWindows"").addEventListener(""click"", () => post(""/Home/LockWindows"").catch(() => {}));
        $(""shutdownNow"").addEventListener(""click"", () => confirmed(""\u786E\u5B9A\u7ACB\u5373\u5173\u673A\uFF1F"", ""/Home/WindowsShutdown""));
        $(""restartNow"").addEventListener(""click"", () => confirmed(""\u786E\u5B9A\u7ACB\u5373\u91CD\u542F\uFF1F"", ""/Home/WindowsRestart""));

        loadDisplays();
        loadVolume();
        send(""/Home/GetShutdownTimingVal"").then(data => {
            const seconds = parseInt(data || 0, 10);
            if (seconds > 0) startCountdown(seconds);
        }).catch(() => {});
    </script>
</body>
</html>";
    }
}
