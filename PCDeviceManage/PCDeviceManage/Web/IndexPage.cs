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
    <title>PC Control Console</title>
    <style>
        :root {
            color-scheme: light dark;
            --bg: #eef3f7;
            --panel: rgba(255, 255, 255, 0.86);
            --panel-solid: #ffffff;
            --text: #10212f;
            --muted: #5d6b78;
            --line: rgba(56, 87, 112, 0.18);
            --accent: #00a6b4;
            --accent-strong: #007989;
            --ok: #35a853;
            --danger: #d54848;
            --shadow: 0 18px 42px rgba(27, 59, 82, 0.16);
            --track: #d5e2ea;
        }

        @media (prefers-color-scheme: dark) {
            :root {
                --bg: #081117;
                --panel: rgba(13, 25, 34, 0.82);
                --panel-solid: #0d1922;
                --text: #e8f5f7;
                --muted: #8ca3ad;
                --line: rgba(126, 212, 221, 0.18);
                --accent: #33d6c8;
                --accent-strong: #7df3df;
                --ok: #76e39a;
                --danger: #ff6d6d;
                --shadow: 0 22px 52px rgba(0, 0, 0, 0.38);
                --track: #1d3742;
            }
        }

        * { box-sizing: border-box; }

        body {
            min-height: 100vh;
            margin: 0;
            color: var(--text);
            font-family: Bahnschrift, ""Segoe UI Variable"", ""Microsoft YaHei UI"", sans-serif;
            background:
                radial-gradient(circle at 10% 10%, rgba(0, 166, 180, 0.18), transparent 32rem),
                radial-gradient(circle at 86% 4%, rgba(53, 168, 83, 0.12), transparent 26rem),
                linear-gradient(135deg, var(--bg), var(--bg));
        }

        body::before {
            position: fixed;
            inset: 0;
            pointer-events: none;
            content: """";
            background-image:
                linear-gradient(var(--line) 1px, transparent 1px),
                linear-gradient(90deg, var(--line) 1px, transparent 1px);
            background-size: 44px 44px;
            mask-image: linear-gradient(to bottom, rgba(0, 0, 0, 0.7), transparent 76%);
        }

        .shell {
            position: relative;
            width: min(1180px, calc(100% - 32px));
            margin: 0 auto;
            padding: 28px 0 34px;
        }

        .topbar {
            display: flex;
            align-items: end;
            justify-content: space-between;
            gap: 18px;
            margin-bottom: 22px;
        }

        .eyebrow {
            margin: 0 0 8px;
            color: var(--accent-strong);
            font-size: 12px;
            font-weight: 700;
            letter-spacing: 0.18em;
        }

        h1, h2, p { margin-top: 0; }

        h1 {
            margin-bottom: 0;
            font-size: clamp(32px, 5vw, 58px);
            line-height: 0.98;
            font-weight: 800;
        }

        h2 {
            margin-bottom: 4px;
            font-size: 20px;
            font-weight: 760;
        }

        .status {
            display: inline-flex;
            align-items: center;
            gap: 10px;
            min-width: 196px;
            padding: 11px 14px;
            border: 1px solid var(--line);
            border-radius: 8px;
            background: var(--panel);
            box-shadow: var(--shadow);
            color: var(--muted);
            font-size: 13px;
        }

        .dot {
            width: 9px;
            height: 9px;
            border-radius: 999px;
            background: var(--ok);
            box-shadow: 0 0 18px var(--ok);
        }

        .grid {
            display: grid;
            grid-template-columns: minmax(0, 1.3fr) minmax(300px, 0.7fr);
            gap: 16px;
        }

        .panel {
            position: relative;
            overflow: hidden;
            min-height: 214px;
            padding: 20px;
            border: 1px solid var(--line);
            border-radius: 8px;
            background: var(--panel);
            box-shadow: var(--shadow);
            backdrop-filter: blur(18px);
        }

        .panel::after {
            position: absolute;
            inset: 0;
            pointer-events: none;
            content: """";
            background: linear-gradient(135deg, rgba(255,255,255,0.16), transparent 26%, transparent 72%, rgba(0,166,180,0.08));
        }

        .panel-wide { grid-row: span 2; }

        .section-head {
            position: relative;
            z-index: 1;
            display: flex;
            align-items: start;
            justify-content: space-between;
            gap: 12px;
            margin-bottom: 18px;
        }

        .section-code {
            color: var(--accent-strong);
            font-size: 11px;
            font-weight: 800;
            letter-spacing: 0.16em;
        }

        .muted {
            color: var(--muted);
            font-size: 13px;
        }

        .device-grid {
            position: relative;
            z-index: 1;
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(230px, 1fr));
            gap: 12px;
        }

        .device-card {
            padding: 16px;
            border: 1px solid var(--line);
            border-radius: 8px;
            background: color-mix(in srgb, var(--panel-solid) 74%, transparent);
        }

        .device-name {
            margin: 0 0 6px;
            font-size: 17px;
            font-weight: 760;
        }

        .device-meta {
            display: flex;
            justify-content: space-between;
            gap: 8px;
            margin-bottom: 16px;
            color: var(--muted);
            font-size: 12px;
        }

        .empty {
            padding: 26px;
            border: 1px dashed var(--line);
            border-radius: 8px;
            color: var(--muted);
            text-align: center;
        }

        .meter-row {
            display: grid;
            grid-template-columns: 1fr auto;
            align-items: center;
            gap: 12px;
        }

        .value {
            min-width: 52px;
            color: var(--accent-strong);
            font-variant-numeric: tabular-nums;
            font-weight: 800;
            text-align: right;
        }

        input[type=""range""] {
            width: 100%;
            accent-color: var(--accent);
        }

        input[type=""number""] {
            width: 100%;
            padding: 10px 12px;
            color: var(--text);
            border: 1px solid var(--line);
            border-radius: 8px;
            outline: none;
            background: var(--panel-solid);
        }

        .stack {
            position: relative;
            z-index: 1;
            display: grid;
            gap: 14px;
        }

        .button-row {
            display: flex;
            flex-wrap: wrap;
            gap: 10px;
        }

        button {
            min-height: 42px;
            padding: 0 14px;
            color: var(--text);
            border: 1px solid var(--line);
            border-radius: 8px;
            background: var(--panel-solid);
            cursor: pointer;
            font: inherit;
            font-weight: 760;
        }

        button.primary {
            color: #001719;
            border-color: transparent;
            background: linear-gradient(135deg, var(--accent), var(--accent-strong));
        }

        button.danger {
            color: #fff;
            border-color: transparent;
            background: linear-gradient(135deg, var(--danger), #8f2424);
        }

        button:hover { transform: translateY(-1px); }

        .countdown {
            min-height: 24px;
            color: var(--accent-strong);
            font-variant-numeric: tabular-nums;
            font-weight: 800;
        }

        @media (max-width: 860px) {
            .topbar { align-items: stretch; flex-direction: column; }
            .status { width: 100%; }
            .grid { grid-template-columns: 1fr; }
            .panel-wide { grid-row: auto; }
        }
    </style>
</head>
<body>
    <div class=""shell"">
        <header class=""topbar"">
            <div>
                <p class=""eyebrow"">PC DEVICE CONSOLE</p>
                <h1>&#x7535;&#x8111;&#x63A7;&#x5236;&#x9762;&#x677F;</h1>
            </div>
            <div class=""status""><span class=""dot""></span><span id=""displayStatus"">&#x7B49;&#x5F85;&#x8BBE;&#x5907;&#x5237;&#x65B0;</span></div>
        </header>

        <main class=""grid"">
            <section class=""panel panel-wide"">
                <div class=""section-head"">
                    <div>
                        <div class=""section-code"">DISPLAY ARRAY</div>
                        <h2>&#x663E;&#x793A;&#x5668;&#x4EAE;&#x5EA6;</h2>
                    </div>
                </div>
                <div id=""displayGrid"" class=""device-grid"">
                    <div class=""empty"">&#x6B63;&#x5728;&#x91CD;&#x65B0;&#x8BFB;&#x53D6;&#x663E;&#x793A;&#x5668;&#x4FE1;&#x606F;</div>
                </div>
            </section>

            <section class=""panel"">
                <div class=""section-head"">
                    <div>
                        <div class=""section-code"">AUDIO BUS</div>
                        <h2>&#x97F3;&#x91CF;&#x63A7;&#x5236;</h2>
                    </div>
                </div>
                <div class=""stack"">
                    <div class=""meter-row"">
                        <input type=""range"" id=""rangeVolume"" min=""0"" max=""100"" value=""0"" />
                        <span class=""value"" id=""volumeValue"">0%</span>
                    </div>
                </div>
            </section>

            <section class=""panel"">
                <div class=""section-head"">
                    <div>
                        <div class=""section-code"">POWER TIMER</div>
                        <h2>&#x5B9A;&#x65F6;&#x5173;&#x673A;</h2>
                    </div>
                </div>
                <div class=""stack"">
                    <div class=""meter-row"">
                        <input type=""range"" id=""shutdownTimingRange"" min=""0"" max=""240"" value=""0"" />
                        <span class=""value"" id=""shutdownRangeValue"">0m</span>
                    </div>
                    <input id=""shutdownTimingInput"" type=""number"" min=""0"" max=""10000"" placeholder=""&#x8F93;&#x5165;&#x5206;&#x949F;&#xFF0C;&#x53EF;&#x8D85;&#x8FC7; 240"" />
                    <div class=""button-row"">
                        <button class=""primary"" id=""applyShutdown"">&#x542F;&#x52A8;&#x5B9A;&#x65F6;</button>
                        <button id=""cancelShutdown"">&#x53D6;&#x6D88;&#x5B9A;&#x65F6;</button>
                    </div>
                    <div class=""countdown"" id=""shutdownTimingMessage""></div>
                </div>
            </section>

            <section class=""panel"">
                <div class=""section-head"">
                    <div>
                        <div class=""section-code"">SYSTEM COMMAND</div>
                        <h2>&#x7CFB;&#x7EDF;&#x63A7;&#x5236;</h2>
                    </div>
                </div>
                <div class=""button-row"">
                    <button class=""primary"" id=""lockWindows"">&#x9501;&#x5B9A;&#x7535;&#x8111;</button>
                    <button class=""danger"" id=""shutdownNow"">&#x7ACB;&#x5373;&#x5173;&#x673A;</button>
                    <button class=""danger"" id=""restartNow"">&#x7ACB;&#x5373;&#x91CD;&#x542F;</button>
                </div>
            </section>
        </main>
    </div>

    <script>
        const displayGrid = document.getElementById(""displayGrid"");
        const displayStatus = document.getElementById(""displayStatus"");
        const volumeRange = document.getElementById(""rangeVolume"");
        const volumeValue = document.getElementById(""volumeValue"");
        const shutdownRange = document.getElementById(""shutdownTimingRange"");
        const shutdownInput = document.getElementById(""shutdownTimingInput"");
        const shutdownRangeValue = document.getElementById(""shutdownRangeValue"");
        const shutdownMessage = document.getElementById(""shutdownTimingMessage"");
        let shutdownSeconds = 0;
        let countdownTimer = null;

        function requestText(url) {
            return fetch(url, { cache: ""no-store"" }).then(response => response.text());
        }

        function setStatus(text) {
            displayStatus.textContent = text;
        }

        function renderDisplays(items) {
            displayGrid.innerHTML = """";
            if (!items || items.length === 0) {
                displayGrid.innerHTML = ""<div class='empty'>&#x672A;&#x68C0;&#x6D4B;&#x5230;&#x663E;&#x793A;&#x5668;&#x4FE1;&#x606F;</div>"";
                setStatus(""\u672A\u68C0\u6D4B\u5230\u663E\u793A\u5668"");
                return;
            }

            items.forEach((item, index) => {
                const card = document.createElement(""article"");
                card.className = ""device-card"";
                const title = document.createElement(""p"");
                title.className = ""device-name"";
                title.textContent = item.AlternateDescription || item.Description || (""Display "" + (index + 1));

                const meta = document.createElement(""div"");
                meta.className = ""device-meta"";
                meta.innerHTML = ""<span>"" + (item.IsInternal ? ""Internal"" : ""External"") + ""</span><span>#"" + (index + 1) + ""</span>"";

                const row = document.createElement(""div"");
                row.className = ""meter-row"";
                const range = document.createElement(""input"");
                range.type = ""range"";
                range.min = ""0"";
                range.max = ""100"";
                range.value = normalizeValue(item.Brightness);
                const value = document.createElement(""span"");
                value.className = ""value"";
                value.textContent = range.value + ""%"";

                range.addEventListener(""input"", () => {
                    value.textContent = range.value + ""%"";
                    requestText(""/Home/SetBrightness?brightness="" + encodeURIComponent(range.value) + ""&IsInternal="" + encodeURIComponent(item.IsInternal));
                });

                row.appendChild(range);
                row.appendChild(value);
                card.appendChild(title);
                card.appendChild(meta);
                card.appendChild(row);
                displayGrid.appendChild(card);
            });

            setStatus(items.length + "" display"" + (items.length > 1 ? ""s"" : """") + "" loaded"");
        }

        function normalizeValue(value) {
            const parsed = parseInt(value, 10);
            if (Number.isNaN(parsed)) return 0;
            return Math.max(0, Math.min(100, parsed));
        }

        function loadDisplays() {
            return requestText(""/Home/GetDisplayDeviceInfo"").then(data => {
                if (!data || data === ""error"") {
                    renderDisplays([]);
                    return;
                }
                renderDisplays(JSON.parse(data));
            }).catch(() => setStatus(""Display loading failed""));
        }

        function loadVolume() {
            requestText(""/Home/GetVolume"").then(data => {
                volumeRange.value = normalizeValue(data);
                volumeValue.textContent = volumeRange.value + ""%"";
            }).catch(() => {});
        }

        function setVolume() {
            volumeValue.textContent = volumeRange.value + ""%"";
            requestText(""/Home/SetVolume?volume="" + encodeURIComponent(volumeRange.value));
        }

        function applyShutdown(minutes) {
            const normalized = Math.max(0, parseInt(minutes || 0, 10));
            shutdownInput.value = normalized;
            shutdownRange.value = Math.min(normalized, parseInt(shutdownRange.max, 10));
            shutdownRangeValue.textContent = normalized + ""m"";
            shutdownSeconds = normalized * 60;
            requestText(""/Home/TimingShutdown?minute="" + encodeURIComponent(shutdownSeconds));
            startCountdown();
        }

        function startCountdown() {
            if (countdownTimer) clearInterval(countdownTimer);
            updateCountdown();
            countdownTimer = setInterval(() => {
                if (shutdownSeconds <= 0) {
                    clearInterval(countdownTimer);
                    countdownTimer = null;
                    return;
                }
                shutdownSeconds -= 1;
                updateCountdown();
            }, 1000);
        }

        function updateCountdown() {
            if (shutdownSeconds <= 0) {
                shutdownMessage.textContent = """";
                return;
            }
            const hours = Math.floor(shutdownSeconds / 3600);
            const minutes = Math.floor((shutdownSeconds % 3600) / 60);
            const seconds = shutdownSeconds % 60;
            shutdownMessage.textContent = ""\u7535\u8111\u5C06\u4E8E "" + hours + ""h "" + String(minutes).padStart(2, ""0"") + ""m "" + String(seconds).padStart(2, ""0"") + ""s \u540E\u5173\u673A"";
        }

        volumeRange.addEventListener(""input"", setVolume);
        shutdownRange.addEventListener(""input"", () => {
            shutdownInput.value = shutdownRange.value;
            shutdownRangeValue.textContent = shutdownRange.value + ""m"";
        });
        shutdownInput.addEventListener(""input"", () => {
            shutdownRange.value = Math.min(parseInt(shutdownInput.value || 0, 10), parseInt(shutdownRange.max, 10));
            shutdownRangeValue.textContent = (shutdownInput.value || 0) + ""m"";
        });
        document.getElementById(""applyShutdown"").addEventListener(""click"", () => applyShutdown(shutdownInput.value || shutdownRange.value));
        document.getElementById(""cancelShutdown"").addEventListener(""click"", () => applyShutdown(0));
        document.getElementById(""lockWindows"").addEventListener(""click"", () => requestText(""/Home/LockWindows""));
        document.getElementById(""shutdownNow"").addEventListener(""click"", () => requestText(""/Home/WindowsShutdown""));
        document.getElementById(""restartNow"").addEventListener(""click"", () => requestText(""/Home/WindowsRestart""));

        loadDisplays();
        loadVolume();
        requestText(""/Home/GetShutdownTimingVal"").then(data => {
            const seconds = parseInt(data || 0, 10);
            if (seconds > 0) {
                shutdownSeconds = seconds;
                shutdownInput.value = Math.ceil(seconds / 60);
                shutdownRange.value = Math.min(Math.ceil(seconds / 60), parseInt(shutdownRange.max, 10));
                shutdownRangeValue.textContent = shutdownInput.value + ""m"";
                startCountdown();
            }
        }).catch(() => {});
    </script>
</body>
</html>";
    }
}
