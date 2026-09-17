namespace MarkLeaf.UI.Agent;

internal static class AgentPanelPage
{
    public const string Html = """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>MarkLeaf Agent</title>
  <style>
    :root {
      color-scheme: light;
      --canvas: #f7f8f7;
      --surface: #ffffff;
      --surface-2: #f1f4f2;
      --surface-3: #e8eeeb;
      --ink: #16211d;
      --muted: #5d6b65;
      --faint: #839089;
      --line: #dce3df;
      --line-strong: #c8d2cd;
      --accent: #176b55;
      --accent-hover: #115945;
      --accent-soft: #e4f1ec;
      --danger: #b3433e;
      --warning: #a76520;
      --focus: #2a7e68;
      --radius: 12px;
      --ease: cubic-bezier(.2,.8,.2,1);
      font-family: "Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", sans-serif;
      font-size: 14px;
    }

    * { box-sizing: border-box; }
    html, body { width: 100%; height: 100%; margin: 0; overflow: hidden; }
    body { background: var(--canvas); color: var(--ink); }
    button, textarea { font: inherit; }
    button { color: inherit; }
    ::selection { background: #bde0d3; color: #0d2b22; }

    .app {
      height: 100%;
      display: grid;
      grid-template-rows: auto auto minmax(0, 1fr);
      background: var(--canvas);
    }

    .topbar {
      min-height: 66px;
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px 10px;
      background: var(--surface);
      border-bottom: 1px solid var(--line);
    }

    .brand-mark {
      width: 34px;
      height: 34px;
      flex: 0 0 34px;
      display: grid;
      place-items: center;
      border-radius: 9px;
      background: var(--accent);
      color: white;
      font: 600 19px/1 Georgia, serif;
    }

    .topbar-copy { min-width: 0; flex: 1; }
    .product-name { font-size: 14px; line-height: 20px; font-weight: 650; letter-spacing: -.01em; }
    .context-name {
      max-width: 100%;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: var(--muted);
      font-size: 12px;
      line-height: 18px;
    }

    .icon-button {
      width: 36px;
      height: 36px;
      display: grid;
      place-items: center;
      flex: 0 0 auto;
      border: 1px solid transparent;
      border-radius: 9px;
      background: transparent;
      cursor: pointer;
      transition: background 160ms var(--ease), border-color 160ms var(--ease);
    }
    .icon-button:hover { background: var(--surface-2); border-color: var(--line); }
    .icon-button:active { background: var(--surface-3); }
    .icon-button svg { width: 18px; height: 18px; fill: none; stroke: currentColor; stroke-width: 1.7; stroke-linecap: round; stroke-linejoin: round; }

    .tabs {
      min-height: 42px;
      display: flex;
      gap: 4px;
      padding: 6px 12px;
      background: var(--surface);
      border-bottom: 1px solid var(--line);
    }
    .tab {
      min-width: 0;
      height: 30px;
      padding: 0 11px;
      border: 0;
      border-radius: 8px;
      background: transparent;
      color: var(--muted);
      cursor: pointer;
      font-size: 13px;
      font-weight: 550;
      transition: background 160ms var(--ease), color 160ms var(--ease);
    }
    .tab:hover { color: var(--ink); background: var(--surface-2); }
    .tab[aria-selected="true"] { color: var(--accent); background: var(--accent-soft); }

    .stage { min-height: 0; position: relative; overflow: hidden; }
    .page { height: 100%; display: none; }
    .page.active { display: block; }

    .agent-page {
      height: 100%;
      display: grid;
      grid-template-rows: minmax(0, 1fr) auto;
    }
    .thread-scroll { min-height: 0; overflow: auto; overscroll-behavior: contain; }
    .thread-content { width: 100%; max-width: 760px; margin: 0 auto; padding: 22px 18px 20px; }

    .welcome { padding: 12px 0 18px; }
    .welcome h1 { margin: 0; font-size: 23px; line-height: 1.25; letter-spacing: -.025em; font-weight: 660; }
    .welcome p { margin: 8px 0 0; max-width: 52ch; color: var(--muted); font-size: 13px; line-height: 1.65; }

    .suggestions { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-top: 18px; }
    .suggestion {
      min-height: 66px;
      padding: 11px 12px;
      text-align: left;
      border: 1px solid var(--line);
      border-radius: var(--radius);
      background: var(--surface);
      cursor: pointer;
      transition: border-color 160ms var(--ease), background 160ms var(--ease), transform 160ms var(--ease);
    }
    .suggestion:hover { border-color: var(--line-strong); background: #fbfcfb; transform: translateY(-1px); }
    .suggestion:active { transform: translateY(0); }
    .suggestion strong { display: block; font-size: 13px; line-height: 19px; font-weight: 620; }
    .suggestion span { display: block; margin-top: 3px; color: var(--muted); font-size: 12px; line-height: 17px; }

    .thread { display: flex; flex-direction: column; gap: 18px; }
    .thread:not(:empty) + .welcome { display: none; }
    .message { min-width: 0; animation: appear 180ms var(--ease); }
    .message-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 7px; }
    .message-role { font-size: 12px; font-weight: 650; color: var(--muted); }
    .message-time { color: var(--faint); font-size: 11px; font-variant-numeric: tabular-nums; }
    .message-body { font-size: 14px; line-height: 1.65; white-space: pre-wrap; overflow-wrap: anywhere; }
    .message.user .message-body {
      padding: 11px 12px;
      border-radius: var(--radius);
      background: var(--surface-2);
    }
    .message.agent .message-body {
      padding: 13px 14px;
      border: 1px solid var(--line);
      border-radius: var(--radius);
      background: var(--surface);
    }
    .message.error .message-body { border-color: #e6c3c0; background: #fff8f7; color: #7e312d; }

    .activity {
      display: flex;
      gap: 10px;
      align-items: flex-start;
      color: var(--muted);
      font-size: 12px;
      line-height: 18px;
      animation: appear 180ms var(--ease);
    }
    .activity-dot { width: 8px; height: 8px; margin-top: 5px; flex: 0 0 8px; border-radius: 50%; background: var(--accent); box-shadow: 0 0 0 4px var(--accent-soft); }
    .activity strong { color: var(--ink); font-weight: 600; }

    .source-row { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 10px; }
    .source-chip {
      max-width: 100%;
      padding: 4px 8px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      border: 1px solid var(--line);
      border-radius: 999px;
      background: var(--surface-2);
      color: var(--muted);
      font-size: 11px;
    }
    .message-actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 10px; }

    .button {
      min-height: 34px;
      padding: 0 12px;
      border: 1px solid var(--line-strong);
      border-radius: 9px;
      background: var(--surface);
      cursor: pointer;
      font-size: 12px;
      font-weight: 600;
      transition: background 160ms var(--ease), border-color 160ms var(--ease), color 160ms var(--ease);
    }
    .button:hover { background: var(--surface-2); border-color: #aebbb4; }
    .button.primary { border-color: var(--accent); background: var(--accent); color: white; }
    .button.primary:hover { border-color: var(--accent-hover); background: var(--accent-hover); }
    .button:disabled { cursor: not-allowed; opacity: .48; }

    .composer-wrap { padding: 0 12px 12px; background: linear-gradient(transparent, var(--canvas) 18%); }
    .composer {
      width: 100%;
      max-width: 760px;
      margin: 0 auto;
      border: 1px solid var(--line-strong);
      border-radius: 14px;
      background: var(--surface);
      box-shadow: 0 10px 28px rgba(25, 43, 35, .08);
      transition: border-color 160ms var(--ease), box-shadow 160ms var(--ease);
    }
    .composer:focus-within { border-color: var(--focus); box-shadow: 0 0 0 3px rgba(42,126,104,.13), 0 10px 28px rgba(25,43,35,.08); }
    .prompt {
      width: 100%;
      min-height: 64px;
      max-height: 150px;
      resize: none;
      padding: 12px 13px 6px;
      border: 0;
      outline: 0;
      background: transparent;
      color: var(--ink);
      line-height: 1.5;
    }
    .prompt::placeholder { color: #7a8781; }
    .composer-bar { display: flex; align-items: center; gap: 8px; min-height: 44px; padding: 5px 7px 7px 9px; }
    .mode-switch { display: flex; align-items: center; padding: 2px; border-radius: 8px; background: var(--surface-2); }
    .mode {
      height: 28px;
      padding: 0 9px;
      border: 0;
      border-radius: 6px;
      background: transparent;
      color: var(--muted);
      cursor: pointer;
      font-size: 11px;
      font-weight: 600;
    }
    .mode[aria-pressed="true"] { background: var(--surface); color: var(--ink); box-shadow: 0 1px 3px rgba(24,38,32,.09); }
    .model { min-width: 0; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--faint); font-size: 11px; text-align: right; }
    .send {
      width: 34px;
      height: 34px;
      margin-left: auto;
      display: grid;
      place-items: center;
      flex: 0 0 34px;
      border: 0;
      border-radius: 9px;
      background: var(--accent);
      color: white;
      cursor: pointer;
      transition: background 160ms var(--ease), opacity 160ms var(--ease);
    }
    .send:hover { background: var(--accent-hover); }
    .send:disabled { cursor: not-allowed; opacity: .42; }
    .send svg { width: 17px; height: 17px; fill: none; stroke: currentColor; stroke-width: 1.9; stroke-linecap: round; stroke-linejoin: round; }
    .send .stop-icon { display: none; }
    .send.cancellable .send-icon { display: none; }
    .send.cancellable .stop-icon { display: block; }

    .content-page { height: 100%; overflow: auto; padding: 22px 18px 28px; }
    .content-inner { width: 100%; max-width: 760px; margin: 0 auto; }
    .page-heading { margin: 0; font-size: 20px; line-height: 1.3; letter-spacing: -.02em; font-weight: 660; }
    .page-description { margin: 7px 0 0; color: var(--muted); font-size: 13px; line-height: 1.6; }
    .toolbar { display: flex; flex-wrap: wrap; gap: 8px; margin: 16px 0 22px; }
    .section { margin-top: 24px; }
    .section-title { margin: 0 0 9px; font-size: 12px; font-weight: 650; color: var(--muted); }
    .list { border-top: 1px solid var(--line); }
    .row { display: grid; grid-template-columns: minmax(0,1fr) auto; gap: 12px; padding: 11px 2px; border-bottom: 1px solid var(--line); }
    .row-title { min-width: 0; overflow-wrap: anywhere; font-size: 13px; line-height: 19px; font-weight: 580; }
    .row-detail { margin-top: 2px; color: var(--muted); font-size: 11px; line-height: 17px; overflow-wrap: anywhere; }
    .status { align-self: start; padding: 3px 7px; border-radius: 999px; background: var(--surface-2); color: var(--muted); font-size: 10px; font-weight: 650; white-space: nowrap; }
    .status.good { background: var(--accent-soft); color: var(--accent); }
    .status.warn { background: #faeddd; color: #8c551c; }
    .status.bad { background: #f8e5e3; color: #923a35; }

    .metrics { display: grid; grid-template-columns: repeat(3,1fr); gap: 1px; margin-top: 18px; overflow: hidden; border: 1px solid var(--line); border-radius: var(--radius); background: var(--line); }
    .metric { padding: 12px 11px; background: var(--surface); }
    .metric-value { font-size: 19px; font-weight: 680; font-variant-numeric: tabular-nums; }
    .metric-label { margin-top: 2px; color: var(--muted); font-size: 10px; line-height: 15px; }

    .empty { padding: 26px 0; color: var(--muted); font-size: 13px; line-height: 1.6; }
    .toast {
      position: fixed;
      left: 50%;
      bottom: 16px;
      z-index: 20;
      max-width: calc(100% - 28px);
      padding: 9px 12px;
      transform: translate(-50%, 12px);
      border: 1px solid var(--line-strong);
      border-radius: 9px;
      background: #1e2a25;
      color: white;
      box-shadow: 0 8px 24px rgba(14,25,20,.2);
      opacity: 0;
      pointer-events: none;
      font-size: 12px;
      transition: opacity 160ms var(--ease), transform 160ms var(--ease);
    }
    .toast.show { opacity: 1; transform: translate(-50%, 0); }

    :focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
    @keyframes appear { from { opacity: 0; transform: translateY(5px); } to { opacity: 1; transform: translateY(0); } }
    @media (prefers-reduced-motion: reduce) { *, *::before, *::after { scroll-behavior: auto !important; animation-duration: 1ms !important; transition-duration: 1ms !important; } }
    @media (max-width: 520px) {
      .topbar { padding-inline: 12px; }
      .tabs { padding-inline: 8px; }
      .tab { flex: 1; padding-inline: 6px; }
      .thread-content, .content-page { padding-inline: 12px; }
      .suggestions { grid-template-columns: 1fr; }
      .suggestion { min-height: 56px; }
      .model { display: none; }
      .metrics { grid-template-columns: 1fr; }
      .metric { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; }
    }
  </style>
</head>
<body>
<div class="app">
  <header class="topbar">
    <div class="brand-mark" aria-hidden="true">M</div>
    <div class="topbar-copy">
      <div class="product-name">MarkLeaf Agent</div>
      <div class="context-name" id="contextName">打开文档或文件夹后开始</div>
    </div>
    <button class="icon-button" id="newTask" type="button" aria-label="新建任务" title="新建任务">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14"/></svg>
    </button>
    <button class="icon-button" id="settings" type="button" aria-label="Agent 设置" title="Agent 设置">
      <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H2.8v-4H3a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1-1.6v-.2h4V3a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1Z"/></svg>
    </button>
  </header>

  <nav class="tabs" aria-label="Agent 工作区">
    <button class="tab" data-page="agent" aria-selected="true" type="button">任务</button>
    <button class="tab" data-page="context" aria-selected="false" type="button">上下文</button>
    <button class="tab" data-page="check" aria-selected="false" type="button">检查</button>
  </nav>

  <main class="stage">
    <section class="page active" id="page-agent">
      <div class="agent-page">
        <div class="thread-scroll" id="threadScroll">
          <div class="thread-content">
            <div class="thread" id="thread"></div>
            <div class="welcome" id="welcome">
              <h1>和文档一起工作</h1>
              <p>Agent 会先读取当前文档和你允许的资料，再规划或执行任务。所有修改都先交给你审阅。</p>
              <div class="suggestions">
                <button class="suggestion" type="button" data-prompt="分析当前文档，列出最值得优先改进的三处，并说明原因。"><strong>审阅当前文档</strong><span>先分析，不直接改写</span></button>
                <button class="suggestion" type="button" data-prompt="根据当前文档和项目资料，补写最缺少的关键章节，并为重要结论标注来源。"><strong>补写关键章节</strong><span>生成可审阅的 Markdown</span></button>
                <button class="suggestion" type="button" data-prompt="检查当前文档的重要结论是否有资料支持，指出证据不足或相互冲突之处。"><strong>核对结论与证据</strong><span>发现缺口与来源冲突</span></button>
                <button class="suggestion" type="button" data-prompt="按照已经导入的任务要求逐项检查当前文档，并给出修改计划。"><strong>按要求逐项检查</strong><span>查看覆盖与遗漏</span></button>
              </div>
            </div>
          </div>
        </div>
        <div class="composer-wrap">
          <div class="composer">
            <textarea class="prompt" id="prompt" rows="2" aria-label="告诉 Agent 要完成的任务" placeholder="描述要完成的文档任务…"></textarea>
            <div class="composer-bar">
              <div class="mode-switch" aria-label="工作方式">
                <button class="mode" type="button" data-mode="plan" aria-pressed="true">规划</button>
                <button class="mode" type="button" data-mode="execute" aria-pressed="false">执行</button>
              </div>
              <div class="model" id="modelName">本地模型</div>
              <button class="send" id="send" type="button" aria-label="发送任务" title="发送任务">
                <svg class="send-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 14-7-4 14-3-6-7-1Z"/><path d="m12 13 7-8"/></svg>
                <svg class="stop-icon" viewBox="0 0 24 24" aria-hidden="true"><rect x="7" y="7" width="10" height="10" rx="1.5"/></svg>
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>

    <section class="page content-page" id="page-context">
      <div class="content-inner">
        <h2 class="page-heading">项目上下文</h2>
        <p class="page-description">只有这里列出的资料会成为 Agent 的依据。项目数据保存在当前工作区的 .markleaf 文件夹。</p>
        <div class="toolbar">
          <button class="button primary" id="importRequirements" type="button">导入任务要求</button>
          <button class="button" id="importSources" type="button">添加资料</button>
        </div>
        <section class="section">
          <h3 class="section-title" id="requirementsTitle">任务要求</h3>
          <div class="list" id="requirementsList"></div>
        </section>
        <section class="section">
          <h3 class="section-title" id="sourcesTitle">资料来源</h3>
          <div class="list" id="sourcesList"></div>
        </section>
      </div>
    </section>

    <section class="page content-page" id="page-check">
      <div class="content-inner">
        <h2 class="page-heading">文档检查</h2>
        <p class="page-description">检查要求覆盖、重要结论的证据，以及需要人工确认的问题。</p>
        <div class="metrics">
          <div class="metric"><div class="metric-value" id="coverageValue">—</div><div class="metric-label">要求覆盖</div></div>
          <div class="metric"><div class="metric-value" id="evidenceValue">—</div><div class="metric-label">证据覆盖</div></div>
          <div class="metric"><div class="metric-value" id="issueValue">—</div><div class="metric-label">需处理</div></div>
        </div>
        <div class="toolbar">
          <button class="button primary" id="runCheck" type="button">运行检查</button>
          <button class="button" id="export" type="button">导出配套材料</button>
        </div>
        <section class="section">
          <h3 class="section-title">检查结果</h3>
          <div class="list" id="issuesList"></div>
        </section>
      </div>
    </section>
  </main>
</div>
<div class="toast" id="toast" role="status" aria-live="polite"></div>

<script>
(function () {
  var state = { mode: 'plan', busy: false, lastAssistant: null };
  var el = function (id) { return document.getElementById(id); };
  var post = function (message) { if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message); };
  var now = function () { return new Date().toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' }); };

  function switchPage(name) {
    document.querySelectorAll('.tab').forEach(function (tab) { tab.setAttribute('aria-selected', String(tab.dataset.page === name)); });
    document.querySelectorAll('.page').forEach(function (page) { page.classList.toggle('active', page.id === 'page-' + name); });
  }

  function scrollThread() {
    requestAnimationFrame(function () { el('threadScroll').scrollTop = el('threadScroll').scrollHeight; });
  }

  function message(role, content, options) {
    options = options || {};
    var wrap = document.createElement('article');
    wrap.className = 'message ' + role;
    var head = document.createElement('div'); head.className = 'message-head';
    var roleEl = document.createElement('div'); roleEl.className = 'message-role'; roleEl.textContent = role === 'user' ? '你' : role === 'error' ? '未完成' : 'MarkLeaf Agent';
    var timeEl = document.createElement('time'); timeEl.className = 'message-time'; timeEl.textContent = now();
    head.append(roleEl, timeEl);
    var body = document.createElement('div'); body.className = 'message-body'; body.textContent = content || '';
    wrap.append(head, body);
    if (options.sources && options.sources.length) {
      var sources = document.createElement('div'); sources.className = 'source-row';
      options.sources.forEach(function (source) { var chip = document.createElement('span'); chip.className = 'source-chip'; chip.textContent = source; sources.appendChild(chip); });
      wrap.appendChild(sources);
    }
    if (options.canApply) {
      var actions = document.createElement('div'); actions.className = 'message-actions';
      var apply = document.createElement('button'); apply.className = 'button primary'; apply.type = 'button'; apply.textContent = '应用到光标处'; apply.addEventListener('click', function () { post({ action: 'apply' }); });
      actions.appendChild(apply); wrap.appendChild(actions); state.lastAssistant = wrap;
    }
    el('thread').appendChild(wrap); scrollThread();
  }

  function activity(label, detail) {
    var row = document.createElement('div'); row.className = 'activity'; row.dataset.transient = 'true';
    var dot = document.createElement('span'); dot.className = 'activity-dot';
    var copy = document.createElement('div');
    var strong = document.createElement('strong'); strong.textContent = label;
    copy.appendChild(strong);
    if (detail) { var text = document.createTextNode(' · ' + detail); copy.appendChild(text); }
    row.append(dot, copy); el('thread').appendChild(row); scrollThread();
  }

  function clearActivities() { document.querySelectorAll('[data-transient="true"]').forEach(function (node) { node.remove(); }); }

  function setBusy(busy, label, cancellable) {
    state.busy = busy;
    el('send').disabled = busy && !cancellable;
    el('send').classList.toggle('cancellable', busy && cancellable);
    el('send').setAttribute('aria-label', busy && cancellable ? '停止任务' : '发送任务');
    el('send').title = busy && cancellable ? '停止任务' : '发送任务';
    el('prompt').disabled = busy;
    document.querySelectorAll('.mode').forEach(function (button) { button.disabled = busy; });
    if (busy && label) activity(label, cancellable ? '可以随时停止' : '请稍候');
    if (!busy) clearActivities();
  }

  function listRows(target, items, emptyText, mapper) {
    target.replaceChildren();
    if (!items || !items.length) { var empty = document.createElement('div'); empty.className = 'empty'; empty.textContent = emptyText; target.appendChild(empty); return; }
    items.forEach(function (item) {
      var data = mapper(item);
      var row = document.createElement('div'); row.className = 'row';
      var copy = document.createElement('div');
      var title = document.createElement('div'); title.className = 'row-title'; title.textContent = data.title;
      copy.appendChild(title);
      if (data.detail) { var detail = document.createElement('div'); detail.className = 'row-detail'; detail.textContent = data.detail; copy.appendChild(detail); }
      var status = document.createElement('span'); status.className = 'status ' + (data.tone || ''); status.textContent = data.status || '';
      row.append(copy, status); target.appendChild(row);
    });
  }

  function renderState(data) {
    el('contextName').textContent = data.documentName || data.projectName || '打开文档或文件夹后开始';
    el('modelName').textContent = data.model || '未配置模型';
    el('requirementsTitle').textContent = '任务要求 · ' + data.requirements.length;
    el('sourcesTitle').textContent = '资料来源 · ' + data.sources.length;
    listRows(el('requirementsList'), data.requirements, '尚未导入任务要求。Agent 仍可处理当前文档。', function (item) { return { title: item.title, detail: item.description, status: item.covered ? '已覆盖' : '待补充', tone: item.covered ? 'good' : 'warn' }; });
    listRows(el('sourcesList'), data.sources, '尚未添加资料。当前文档会作为默认上下文。', function (item) { return { title: item.name, detail: item.kind + ' · ' + item.status, status: item.trust + '可信度', tone: item.ready ? 'good' : 'warn' }; });
    if (data.check) {
      el('coverageValue').textContent = data.check.coverage + '%';
      el('evidenceValue').textContent = data.check.evidence + '%';
      el('issueValue').textContent = String(data.check.errors);
      listRows(el('issuesList'), data.check.issues, '没有发现需要处理的问题。', function (item) { return { title: item.title, detail: item.detail, status: item.state, tone: item.tone }; });
    } else {
      el('coverageValue').textContent = '—'; el('evidenceValue').textContent = '—'; el('issueValue').textContent = '—';
      listRows(el('issuesList'), [], '打开文档后运行检查。', function () { return {}; });
    }
  }

  var toastTimer;
  function toast(text) { clearTimeout(toastTimer); el('toast').textContent = text; el('toast').classList.add('show'); toastTimer = setTimeout(function () { el('toast').classList.remove('show'); }, 2600); }

  function sendPrompt() {
    var value = el('prompt').value.trim();
    if (!value || state.busy) return;
    switchPage('agent'); message('user', value); el('prompt').value = ''; post({ action: 'send', prompt: value, mode: state.mode });
  }

  document.querySelectorAll('.tab').forEach(function (tab) { tab.addEventListener('click', function () { switchPage(tab.dataset.page); }); });
  document.querySelectorAll('.mode').forEach(function (button) { button.addEventListener('click', function () { state.mode = button.dataset.mode; document.querySelectorAll('.mode').forEach(function (item) { item.setAttribute('aria-pressed', String(item === button)); }); }); });
  document.querySelectorAll('.suggestion').forEach(function (button) { button.addEventListener('click', function () { el('prompt').value = button.dataset.prompt; el('prompt').focus(); }); });
  el('send').addEventListener('click', function () { if (state.busy) post({ action: 'cancel' }); else sendPrompt(); });
  el('prompt').addEventListener('keydown', function (event) { if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); sendPrompt(); } });
  el('newTask').addEventListener('click', function () { el('thread').replaceChildren(); state.lastAssistant = null; el('prompt').focus(); });
  el('settings').addEventListener('click', function () { post({ action: 'settings' }); });
  el('importRequirements').addEventListener('click', function () { post({ action: 'import_requirements' }); });
  el('importSources').addEventListener('click', function () { post({ action: 'import_sources' }); });
  el('runCheck').addEventListener('click', function () { post({ action: 'run_check' }); });
  el('export').addEventListener('click', function () { post({ action: 'export' }); });

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', function (event) {
      var data = event.data || {};
      if (data.type === 'state') renderState(data);
      if (data.type === 'busy') setBusy(Boolean(data.busy), data.label || '正在处理', Boolean(data.cancellable));
      if (data.type === 'activity') activity(data.label || '正在处理', data.detail || '');
      if (data.type === 'assistant') { clearActivities(); message('agent', data.content, { sources: data.sources, canApply: data.canApply }); }
      if (data.type === 'error') { clearActivities(); message('error', data.message || '任务没有完成'); }
      if (data.type === 'toast') toast(data.message || '已完成');
      if (data.type === 'focus') { switchPage('agent'); el('prompt').focus(); }
      if (data.type === 'applied' && state.lastAssistant) { var button = state.lastAssistant.querySelector('.button.primary'); if (button) { button.disabled = true; button.textContent = '已应用，可在编辑器撤销'; } }
    });
  }
  post({ action: 'ready' });
})();
</script>
</body>
</html>
""";
}
