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
      font-family: "Noto Sans SC", "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", sans-serif;
      font-size: 14px;
    }

    * { box-sizing: border-box; }
    html, body { width: 100%; min-width: 0; height: 100%; margin: 0; overflow: hidden; }
    body { background: var(--canvas); color: var(--ink); }
    button, textarea, input, select { font: inherit; }
    button { color: inherit; }
    ::selection { background: #bde0d3; color: #0d2b22; }

    .app {
      min-width: 0;
      height: 100%;
      display: grid;
      grid-template-rows: auto minmax(0, 1fr);
      background: var(--surface);
    }

    .topbar {
      display: none !important;
      min-height: 58px;
      align-items: center;
      gap: 12px;
      padding: 9px 14px;
      background: var(--surface);
      border-bottom: 1px solid var(--line);
    }

    .brand-mark {
      width: 30px;
      height: 30px;
      flex: 0 0 30px;
      display: grid;
      place-items: center;
      border: 1px solid var(--line-strong);
      border-radius: 8px;
      background: var(--surface);
      color: var(--accent);
      font: italic 600 18px/1 Georgia, serif;
    }

    .topbar-copy { min-width: 0; flex: 1; }
    .product-name { font-size: 13px; line-height: 19px; font-weight: 680; letter-spacing: -.01em; }
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
      min-height: 52px;
      display: flex;
      align-items: stretch;
      gap: 0;
      padding: 0 9px;
      background: var(--surface);
      border-bottom: 1px solid var(--line);
    }
    .tab {
      min-width: 0;
      height: 51px;
      padding: 0 10px;
      border: 0;
      border-bottom: 3px solid transparent;
      border-radius: 0;
      background: transparent;
      color: var(--muted);
      cursor: pointer;
      font-size: 12px;
      font-weight: 550;
      transition: background 160ms var(--ease), color 160ms var(--ease);
    }
    .tab:hover { color: var(--ink); background: transparent; }
    .tab[aria-selected="true"] { color: var(--accent); border-bottom-color: var(--accent); background: transparent; font-weight: 700; }
    .stage { min-width: 0; min-height: 0; position: relative; overflow: hidden; }
    .page { min-width: 0; height: 100%; display: none; }
    .page.active { display: block; }

    .agent-page {
      min-width: 0;
      height: 100%;
      display: grid;
      grid-template-rows: minmax(0, 1fr) auto;
    }
    .thread-scroll { min-width: 0; min-height: 0; overflow: auto; overflow-x: hidden; overscroll-behavior: contain; scrollbar-gutter: stable; }
    .thread-content { min-width: 0; width: 100%; max-width: 760px; margin: 0 auto; padding: 22px 16px 20px; }

    .welcome { padding: 12px 0 18px; }
    .welcome h1 { margin: 0; font-size: 20px; line-height: 1.3; letter-spacing: -.02em; font-weight: 700; }
    .welcome p { margin: 8px 0 0; max-width: 52ch; color: var(--muted); font-size: 13px; line-height: 1.65; }

    .task-progress { margin-top: 19px; border-left: 1px solid #bcd8ce; }
    .task-step { position: relative; display: grid; grid-template-columns: minmax(0,1fr) auto; gap: 8px; padding: 0 2px 18px 28px; }
    .task-step > div { min-width: 0; }
    .task-step::before { content: attr(data-step); position: absolute; left: -11px; top: 0; width: 21px; height: 21px; display: grid; place-items: center; border-radius: 50%; background: var(--accent-soft); color: var(--accent); font-size: 11px; font-weight: 800; }
    .task-step.ready::before { content: '✓'; background: var(--accent); color: white; }
    .task-step strong { font-size: 12px; line-height: 1.45; }
    .task-step span { display: block; margin-top: 2px; color: var(--muted); font-size: 10px; line-height: 1.45; }
    .task-step time { color: var(--faint); font-size: 10px; }
    .suggestions { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-top: 8px; }
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
    .message-body { font-size: 14px; line-height: 1.65; overflow-wrap: anywhere; }
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
    .message.user .message-body, .message.error .message-body { white-space: pre-wrap; }
    .markdown-body > :first-child { margin-top: 0; }
    .markdown-body > :last-child { margin-bottom: 0; }
    .markdown-body h1, .markdown-body h2, .markdown-body h3, .markdown-body h4 {
      margin: 1.15em 0 .5em; color: var(--ink); line-height: 1.35; font-weight: 700;
    }
    .markdown-body h1 { font-size: 18px; }
    .markdown-body h2 { font-size: 16px; }
    .markdown-body h3, .markdown-body h4 { font-size: 14px; }
    .markdown-body p { margin: .55em 0; }
    .markdown-body ul, .markdown-body ol { margin: .55em 0; padding-left: 1.6em; }
    .markdown-body li { margin: .25em 0; }
    .markdown-body blockquote { margin: .7em 0; padding: 2px 0 2px 12px; border-left: 2px solid #9dbdaf; color: var(--muted); }
    .markdown-body pre { margin: .75em 0; padding: 10px 11px; overflow: auto; border: 1px solid var(--line); border-radius: 8px; background: #f5f7f6; }
    .markdown-body code { padding: 1px 4px; border-radius: 4px; background: #eef2f0; font-family: "Cascadia Code", Consolas, monospace; font-size: .9em; }
    .markdown-body pre code { padding: 0; background: transparent; }
    .markdown-body table { width: 100%; margin: .75em 0; border-collapse: collapse; font-size: 12px; }
    .markdown-body th, .markdown-body td { padding: 7px 8px; border: 1px solid var(--line); text-align: left; vertical-align: top; }
    .markdown-body th { background: var(--surface-2); color: var(--ink); font-weight: 650; }
    .markdown-body a, .source-ref { color: var(--accent); font-weight: 650; text-decoration: none; }
    .source-ref[role="button"] { cursor: pointer; border-radius: 4px; }
    .source-ref[role="button"]:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
    .markdown-body hr { height: 1px; margin: 1em 0; border: 0; background: var(--line); }
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
      appearance: none;
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
      cursor: pointer;
    }
    .source-chip:hover { border-color: var(--accent); color: var(--accent); background: var(--accent-soft); }
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

    .composer-wrap { position: relative; padding: 0 12px 12px; background: linear-gradient(transparent, var(--canvas) 18%); }
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
    .composer:focus-within { border-color: var(--line-strong); box-shadow: 0 8px 22px rgba(25,43,35,.08); }
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
    .prompt:focus-visible { outline: 0; box-shadow: none; }
    .composer-bar { width: 100%; min-width: 0; display: grid; grid-template-columns: minmax(84px, 1.25fr) 72px minmax(68px, .85fr) 34px; align-items: center; gap: 6px; min-height: 44px; padding: 5px 7px 7px 9px; }
    .compact-select {
      min-width: 0; height: 30px; padding: 0 25px 0 8px; border: 1px solid transparent; border-radius: 7px;
      background: var(--surface-2); color: var(--muted); cursor: pointer; font-size: 11px; font-weight: 600;
    }
    .compact-select:hover, .compact-select:focus { border-color: var(--line-strong); color: var(--ink); outline: 0; }
    .target-select { width: 100%; max-width: none; }
    .mode-select { width: 100%; }
    .model-button {
      min-width: 0;
      width: 100%;
      max-width: none;
      height: 28px;
      margin-left: 0;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 0 8px;
      overflow: hidden;
      border: 1px solid transparent;
      border-radius: 7px;
      background: transparent;
      color: var(--muted);
      cursor: pointer;
      font-size: 11px;
    }
    .model-button:hover, .model-button[aria-expanded="true"] { border-color: var(--line); background: var(--surface-2); color: var(--ink); }
    .model-button .model-text { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: "Cascadia Code", "Consolas", monospace; }
    .model-button svg { width: 13px; height: 13px; flex: 0 0 auto; fill: none; stroke: currentColor; stroke-width: 1.8; }
    .cloud-consent { display: none; align-items: center; gap: 8px; margin: 0 12px 8px; padding: 10px 11px; border: 1px solid #c8d8d1; border-radius: 10px; background: #f3f8f6; }
    .cloud-consent.show { display: flex; }
    .cloud-consent-copy { min-width: 0; flex: 1; color: var(--muted); font-size: 11px; line-height: 1.5; }
    .cloud-consent-copy strong { display: block; margin-bottom: 2px; color: var(--ink); font-size: 12px; }
    .cloud-consent-actions { display: flex; gap: 6px; flex: 0 0 auto; }

    .model-picker {
      position: absolute;
      left: 12px;
      right: 12px;
      bottom: calc(100% - 3px);
      z-index: 12;
      max-width: 420px;
      margin: 0 auto;
      overflow: hidden;
      border: 1px solid var(--line-strong);
      border-radius: 13px;
      background: var(--surface);
      box-shadow: 0 18px 48px rgba(15, 32, 25, .16), 0 2px 8px rgba(15, 32, 25, .08);
      opacity: 0;
      pointer-events: none;
      transform: translateY(8px) scale(.99);
      transform-origin: bottom center;
      transition: opacity 160ms var(--ease), transform 160ms var(--ease);
    }
    .model-picker.open { opacity: 1; pointer-events: auto; transform: translateY(0) scale(1); }
    .model-search-wrap { padding: 10px; border-bottom: 1px solid var(--line); }
    .model-search { width: 100%; height: 36px; padding: 0 11px; border: 1px solid var(--line-strong); border-radius: 8px; outline: 0; background: var(--canvas); color: var(--ink); font-size: 12px; }
    .model-search:focus { border-color: var(--focus); box-shadow: 0 0 0 3px rgba(42,126,104,.12); background: var(--surface); }
    .model-group { max-height: 230px; overflow: auto; padding: 6px; }
    .model-group-label { padding: 7px 8px 4px; color: var(--faint); font-size: 10px; font-weight: 680; letter-spacing: .04em; text-transform: uppercase; }
    .model-option { width: 100%; min-height: 38px; display: grid; grid-template-columns: 18px minmax(0,1fr) auto; align-items: center; gap: 7px; padding: 7px 8px; border: 0; border-radius: 7px; background: transparent; text-align: left; cursor: pointer; }
    .model-option:hover, .model-option.active { background: var(--surface-2); }
    .model-check { color: var(--accent); font-weight: 750; }
    .model-id { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font: 11px/1.4 "Cascadia Code", "Consolas", monospace; }
    .model-provider { color: var(--faint); font-size: 10px; white-space: nowrap; }
    .model-empty { padding: 18px 10px; color: var(--muted); text-align: center; font-size: 12px; }
    .model-actions { display: flex; border-top: 1px solid var(--line); padding: 6px; }
    .model-action { flex: 1; min-height: 34px; border: 0; border-radius: 7px; background: transparent; color: var(--accent); cursor: pointer; font-size: 11px; font-weight: 650; }
    .model-action:hover { background: var(--accent-soft); }

    .modal-backdrop { position: fixed; inset: 0; z-index: 30; display: grid; place-items: center; padding: 20px; background: rgba(16,31,25,.22); backdrop-filter: blur(2px); opacity: 0; pointer-events: none; transition: opacity 160ms var(--ease); }
    .modal-backdrop.open { opacity: 1; pointer-events: auto; }
    .settings-card { width: min(580px, 100%); max-height: min(720px, calc(100vh - 40px)); display: flex; flex-direction: column; overflow: hidden; border: 1px solid rgba(182,199,191,.82); border-radius: 16px; background: var(--surface); box-shadow: 0 28px 80px rgba(12,28,21,.2), 0 4px 16px rgba(12,28,21,.08); transform: translateY(8px); transition: transform 160ms var(--ease); }
    .modal-backdrop.open .settings-card { transform: translateY(0); }
    .settings-head { display: flex; align-items: flex-start; gap: 12px; padding: 21px 22px 16px; border-bottom: 1px solid var(--line); }
    .settings-title { flex: 1; }
    .settings-title h2 { margin: 0; font-size: 17px; line-height: 1.35; letter-spacing: -.015em; }
    .settings-title p { margin: 4px 0 0; color: var(--muted); font-size: 11px; line-height: 1.55; }
    .settings-body { overflow: auto; padding: 18px 22px 22px; }
    .preset-row { display: grid; grid-template-columns: 1fr 1fr; gap: 3px; margin-bottom: 20px; padding: 3px; border-radius: 11px; background: var(--surface-2); }
    .preset { min-height: 54px; padding: 9px 11px; border: 0; border-radius: 8px; background: transparent; text-align: left; cursor: pointer; }
    .preset:hover { background: rgba(255,255,255,.66); }
    .preset[aria-pressed="true"] { background: var(--surface); box-shadow: 0 1px 4px rgba(20,39,31,.1); }
    .preset strong { display: block; font-size: 12px; }
    .preset span { display: block; margin-top: 2px; color: var(--muted); font-size: 10px; }
    .field { margin-top: 13px; }
    .field label { display: block; margin-bottom: 6px; color: var(--muted); font-size: 11px; font-weight: 650; }
    .field input, .field select { width: 100%; height: 41px; padding: 0 11px; border: 1px solid var(--line-strong); border-radius: 9px; outline: 0; background: #fbfcfb; color: var(--ink); }
    .field input:focus, .field select:focus { border-color: #7e9c90; box-shadow: 0 0 0 3px rgba(88,125,110,.11); background: var(--surface); }
    .field-hint { margin-top: 5px; color: var(--faint); font-size: 10px; line-height: 1.5; }
    .key-row { display: grid; grid-template-columns: minmax(0,1fr) auto; gap: 7px; }
    .key-row .button { height: 38px; }
    .check-row { display: flex; align-items: flex-start; gap: 8px; margin-top: 15px; color: var(--muted); font-size: 11px; line-height: 1.5; }
    .check-row input { margin-top: 2px; accent-color: var(--accent); }
    .connection-status { display: none; margin-top: 14px; padding: 10px 11px; border: 1px solid var(--line); border-radius: 9px; background: var(--canvas); color: var(--muted); font-size: 11px; line-height: 1.5; }
    .connection-status.show { display: block; }
    .connection-status.good { border-color: #b9d8cc; background: var(--accent-soft); color: var(--accent); }
    .connection-status.bad { border-color: #e6c3c0; background: #fff6f5; color: #8e3732; }
    .settings-actions { display: flex; justify-content: space-between; gap: 8px; padding: 13px 22px; border-top: 1px solid var(--line); background: var(--surface); }
    .settings-actions-right { display: flex; gap: 8px; }
    .send {
      width: 34px;
      height: 34px;
      margin-left: auto;
      display: grid;
      place-items: center;
      min-width: 34px;
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
    .row.clickable { cursor: pointer; border-radius: 8px; padding-inline: 7px; margin-inline: -7px; }
    .row.clickable:hover { background: var(--accent-soft); }
    .row.clickable:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
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

    .deliverables { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-top: 18px; }
    .deliverable { min-height: 78px; padding: 12px; border: 1px solid var(--line); border-radius: 11px; background: var(--surface); }
    .deliverable-icon { color: var(--accent); font-size: 12px; font-weight: 750; }
    .deliverable strong { display: block; margin-top: 8px; font-size: 12px; }
    .deliverable span { display: block; margin-top: 3px; color: var(--muted); font-size: 10px; line-height: 1.45; }

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

    :focus-visible { outline: 2px solid #48645b; outline-offset: 2px; }
    @keyframes appear { from { opacity: 0; transform: translateY(5px); } to { opacity: 1; transform: translateY(0); } }
    @media (prefers-reduced-motion: reduce) { *, *::before, *::after { scroll-behavior: auto !important; animation-duration: 1ms !important; transition-duration: 1ms !important; } }
    @media (max-width: 520px) {
      .topbar { padding-inline: 12px; }
      .tabs { padding-inline: 8px; }
      .tab { flex: 1; padding-inline: 6px; }
      .thread-content, .content-page { padding-inline: 12px; }
      .suggestions { grid-template-columns: 1fr; }
      .suggestion { min-height: 56px; }
      .composer-wrap { padding-inline: 8px; }
      .composer-bar { grid-template-columns: minmax(80px, 1fr) 68px minmax(64px, .8fr) 34px; gap: 4px; padding-inline: 7px; }
      .compact-select { padding-left: 6px; padding-right: 19px; font-size: 10px; }
      .model-button { padding-inline: 5px; font-size: 10px; }
      .metrics { grid-template-columns: 1fr; }
      .metric { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; }
      .deliverables { grid-template-columns: 1fr; }
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

  <nav class="tabs" aria-label="智能工作区">
    <button class="tab" data-page="agent" aria-selected="true" type="button">对话</button>
    <button class="tab" data-page="context" aria-selected="false" type="button">证据</button>
    <button class="tab" data-page="check" aria-selected="false" type="button">审阅</button>
  </nav>

  <main class="stage">
    <section class="page active" id="page-agent">
      <div class="agent-page">
        <div class="thread-scroll" id="threadScroll">
          <div class="thread-content">
            <div class="thread" id="thread"></div>
            <div class="welcome" id="welcome">
              <h1>文档协作台</h1>
              <p>围绕选定的主文档组织资料、要求、证据和修改；每一处改动先预览，再由你确认写入。</p>
              <div class="task-progress">
                <div class="task-step" id="documentStep" data-step="1"><div><strong id="documentStepTitle">等待主文档</strong><span id="documentStepDetail">打开 Markdown 文档后识别结构和关键结论</span></div><time id="documentStepStatus">等待</time></div>
                <div class="task-step" id="sourceStep" data-step="2"><div><strong id="sourceStepTitle">尚未添加资料</strong><span id="sourceStepDetail">打开项目文件夹后自动索引本地资料</span></div><time id="sourceStepStatus">等待</time></div>
                <div class="task-step" data-step="3"><div><strong>等待你的任务</strong><span>直接提问，或生成可审阅的章节修改</span></div><time>现在</time></div>
              </div>
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
          <div class="cloud-consent" id="cloudConsent" role="status" aria-live="polite">
            <div class="cloud-consent-copy"><strong id="cloudConsentTitle">允许云端处理资料</strong><span id="cloudConsentText"></span></div>
            <div class="cloud-consent-actions"><button class="button" id="denyCloud" type="button">取消</button><button class="button primary" id="allowCloud" type="button">允许本次会话</button></div>
          </div>
          <div class="composer">
            <textarea class="prompt" id="prompt" rows="2" aria-label="告诉 Agent 要完成的任务" placeholder="描述要完成的文档任务…"></textarea>
            <div class="composer-bar">
              <select class="compact-select target-select" id="targetDocument" aria-label="操作的主文档" title="操作的主文档"></select>
              <select class="compact-select mode-select" id="modeSelect" aria-label="Agent 工作方式" title="Agent 工作方式"><option value="auto">自动</option><option value="readonly">只读回答</option><option value="plan">只做方案</option></select>
              <button class="model-button" id="modelButton" type="button" aria-haspopup="listbox" aria-expanded="false">
                <span class="model-text" id="modelName">本地模型</span>
                <svg viewBox="0 0 16 16" aria-hidden="true"><path d="m4 10 4-4 4 4"/></svg>
              </button>
              <button class="send" id="send" type="button" aria-label="发送任务" title="发送任务">
                <svg class="send-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 14-7-4 14-3-6-7-1Z"/><path d="m12 13 7-8"/></svg>
                <svg class="stop-icon" viewBox="0 0 24 24" aria-hidden="true"><rect x="7" y="7" width="10" height="10" rx="1.5"/></svg>
              </button>
            </div>
          </div>
          <div class="model-picker" id="modelPicker" role="dialog" aria-label="选择模型">
            <div class="model-search-wrap"><input class="model-search" id="modelSearch" type="search" placeholder="搜索模型" autocomplete="off" aria-label="搜索模型"></div>
            <div class="model-group" id="modelList" role="listbox"></div>
            <div class="model-actions">
              <button class="model-action" id="addProvider" type="button">＋ 添加 API 提供商</button>
              <button class="model-action" id="manageModels" type="button">管理模型…</button>
            </div>
          </div>
        </div>
      </div>
    </section>

    <section class="page content-page" id="page-context">
      <div class="content-inner">
        <h2 class="page-heading">证据库</h2>
        <p class="page-description">项目资料会自动建立索引，回答中显示准确的文件、行号或 PDF 页码。</p>
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
        <h2 class="page-heading">审阅队列</h2>
        <p class="page-description" id="checkDescription">集中查看要求覆盖、证据缺口和等待确认的章节修改。</p>
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

    <section class="page content-page" id="page-deliver">
      <div class="content-inner">
        <h2 class="page-heading">交付与透明记录</h2>
        <p class="page-description">从同一份 Markdown 项目生成配套材料，并保留 AI 建议与人工采纳记录。</p>
        <div class="deliverables">
          <div class="deliverable"><div class="deliverable-icon">01</div><strong>文档体检报告</strong><span>要求覆盖、证据覆盖与待处理问题</span></div>
          <div class="deliverable"><div class="deliverable-icon">02</div><strong>AI 使用说明</strong><span>资料范围、操作记录与责任声明</span></div>
          <div class="deliverable"><div class="deliverable-icon">03</div><strong>答辩提纲</strong><span>演示路径、创新价值与可能追问</span></div>
          <div class="deliverable"><div class="deliverable-icon">04</div><strong>调研工具包</strong><span>访谈提纲、可用性任务与记录表</span></div>
        </div>
        <div class="toolbar"><button class="button primary" id="exportPackage" type="button">生成全部配套材料</button></div>
        <section class="section"><h3 class="section-title" id="auditsTitle">AI 与人工操作记录</h3><div class="list" id="auditsList"></div></section>
      </div>
    </section>

    <section class="page content-page" id="page-help">
      <div class="content-inner">
        <h2 class="page-heading">MarkLeaf Agent 使用帮助</h2>
        <p class="page-description">在同一个工作台里编辑 Markdown、整理资料，并让 Agent 完成可审阅的文档任务。</p>
        <section class="section">
          <h3 class="section-title">快速开始</h3>
          <div class="list">
            <div class="row"><div><div class="row-title">打开项目文件夹</div><div class="row-detail">左侧会显示 Markdown、PDF、图片和其他项目资料。</div></div></div>
            <div class="row"><div><div class="row-title">告诉 Agent 你的目标</div><div class="row-detail">直接描述任务即可；只有需要方案时才选择“只做方案”，修改会先进入审阅状态。</div></div></div>
            <div class="row"><div><div class="row-title">配置本地或云端模型</div><div class="row-detail">点击顶部模型名称，或右上角设置按钮接入自己的 API。</div></div></div>
          </div>
        </section>
        <section class="section">
          <h3 class="section-title">数据与安全</h3>
          <p class="page-description">本地模型不会把资料发送到互联网。使用云端 API 时，软件会在发送项目信息前进行确认。</p>
        </section>
      </div>
    </section>
  </main>
</div>
<div class="modal-backdrop" id="settingsModal" role="presentation">
  <section class="settings-card" role="dialog" aria-modal="true" aria-labelledby="settingsTitle">
    <header class="settings-head">
      <div class="settings-title"><h2 id="settingsTitle">模型与 API</h2><p>接入本地 Ollama，或任何兼容 OpenAI Chat Completions 的服务。</p></div>
      <button class="icon-button" id="closeSettings" type="button" aria-label="关闭设置" title="关闭"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18"/></svg></button>
    </header>
    <div class="settings-body">
      <div class="preset-row" aria-label="提供商类型">
        <button class="preset" type="button" data-provider="ollama" aria-pressed="true"><strong>Ollama · 本地</strong><span>资料不离开电脑，无需 API Key</span></button>
        <button class="preset" type="button" data-provider="openai-compatible" aria-pressed="false"><strong>自定义 API</strong><span>OpenAI、DeepSeek 或兼容服务</span></button>
      </div>
      <div class="field"><label for="providerName">显示名称</label><input id="providerName" type="text" autocomplete="off" placeholder="例如：我的 DeepSeek"></div>
      <div class="field"><label for="endpoint">API Base URL</label><input id="endpoint" type="url" spellcheck="false" autocomplete="off" placeholder="https://api.example.com/v1"><div class="field-hint">填写到 /v1 即可，MarkLeaf 会自动请求 /models 和 /chat/completions。</div></div>
      <div class="field"><label for="apiKey">API Key</label><div class="key-row"><input id="apiKey" type="password" spellcheck="false" autocomplete="off" placeholder="本地 Ollama 可留空"><button class="button" id="toggleKey" type="button">显示</button></div><div class="field-hint" id="keyHint">Key 由 Windows 当前账户加密保存，不会写入文档、项目或导出文件。</div></div>
      <div class="field"><label for="modelInput">模型 ID</label><input id="modelInput" type="text" list="discoveredModels" spellcheck="false" autocomplete="off" placeholder="例如 qwen3:4b"><datalist id="discoveredModels"></datalist><div class="field-hint">可以手动填写，或先测试连接后从发现的模型中选择。</div></div>
      <label class="check-row"><input id="confirmCloud" type="checkbox" checked><span>每次启动软件时，首次发送资料到云端前确认一次</span></label>
      <div class="connection-status" id="connectionStatus" role="status" aria-live="polite"></div>
    </div>
    <footer class="settings-actions">
      <button class="button" id="testConnection" type="button">测试连接</button>
      <div class="settings-actions-right"><button class="button" id="cancelSettings" type="button">取消</button><button class="button primary" id="saveSettings" type="button">保存并使用</button></div>
    </footer>
  </section>
</div>
<div class="toast" id="toast" role="status" aria-live="polite"></div>

<script>
(function () {
  var state = { mode: 'auto', busy: false, lastAssistant: null, lastPrompt: '', targetPath: '', model: '', providerName: '', providerType: 'ollama', endpoint: '', apiKeyConfigured: false, confirmBeforeCloud: true, availableModels: [], documents: [], settingsProviderType: 'ollama' };
  var el = function (id) { return document.getElementById(id); };
  var post = function (message) { if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message); };
  var now = function () { return new Date().toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' }); };

  function switchPage(name) {
    document.querySelectorAll('.tab').forEach(function (tab) { tab.setAttribute('aria-selected', String(tab.dataset.page === name)); });
    document.querySelectorAll('.page').forEach(function (page) { page.classList.toggle('active', page.id === 'page-' + name); });
  }

  function closeModelPicker() {
    el('modelPicker').classList.remove('open');
    el('modelButton').setAttribute('aria-expanded', 'false');
  }

  function renderModels(query) {
    var value = (query || '').trim().toLocaleLowerCase();
    var models = (state.availableModels || []).filter(function (model) { return !value || model.toLocaleLowerCase().includes(value); });
    var list = el('modelList'); list.replaceChildren();
    var label = document.createElement('div'); label.className = 'model-group-label'; label.textContent = state.providerName || '已连接的模型'; list.appendChild(label);
    if (!models.length) { var empty = document.createElement('div'); empty.className = 'model-empty'; empty.textContent = value ? '没有匹配的模型' : '测试连接后会在这里显示模型'; list.appendChild(empty); return; }
    models.forEach(function (model) {
      var button = document.createElement('button'); button.className = 'model-option' + (model === state.model ? ' active' : ''); button.type = 'button'; button.setAttribute('role', 'option'); button.setAttribute('aria-selected', String(model === state.model));
      var check = document.createElement('span'); check.className = 'model-check'; check.textContent = model === state.model ? '✓' : '';
      var id = document.createElement('span'); id.className = 'model-id'; id.textContent = model;
      var provider = document.createElement('span'); provider.className = 'model-provider'; provider.textContent = state.providerName || '';
      button.append(check, id, provider);
      button.addEventListener('click', function () { state.model = model; el('modelName').textContent = model; post({ action: 'select_model', model: model }); closeModelPicker(); });
      list.appendChild(button);
    });
  }

  function applyProvider(type, useDefaults) {
    state.settingsProviderType = type;
    document.querySelectorAll('.preset').forEach(function (button) { button.setAttribute('aria-pressed', String(button.dataset.provider === type)); });
    if (!useDefaults) return;
    if (type === 'ollama') {
      el('providerName').value = 'Ollama'; el('endpoint').value = 'http://localhost:11434/v1'; el('apiKey').value = ''; el('modelInput').value = state.providerType === 'ollama' ? state.model : 'qwen3:4b';
    } else {
      el('providerName').value = '自定义 API'; el('endpoint').value = ''; el('apiKey').value = ''; el('modelInput').value = '';
    }
    el('connectionStatus').className = 'connection-status'; el('connectionStatus').textContent = '';
  }

  function openSettings() {
    closeModelPicker();
    el('providerName').value = state.providerName || (state.providerType === 'ollama' ? 'Ollama' : '自定义 API');
    el('endpoint').value = state.endpoint || '';
    el('apiKey').value = '';
    el('apiKey').placeholder = state.apiKeyConfigured ? '已安全保存；留空保持不变' : (state.providerType === 'ollama' ? '本地 Ollama 可留空' : 'sk-…');
    el('modelInput').value = state.model || '';
    el('confirmCloud').checked = state.confirmBeforeCloud !== false;
    el('connectionStatus').className = 'connection-status'; el('connectionStatus').textContent = '';
    applyProvider(state.providerType || 'openai-compatible', false);
    el('settingsModal').classList.add('open');
    setTimeout(function () { el('providerName').focus(); }, 20);
  }

  function closeSettings() { el('settingsModal').classList.remove('open'); }

  function connectionStatus(messageText, tone) {
    var target = el('connectionStatus'); target.textContent = messageText; target.className = 'connection-status show ' + (tone || '');
  }

  function connectionPayload(action) {
    return { action: action, providerType: state.settingsProviderType, providerName: el('providerName').value.trim(), endpoint: el('endpoint').value.trim(), apiKey: el('apiKey').value, model: el('modelInput').value.trim(), confirmBeforeCloud: el('confirmCloud').checked };
  }

  function connectionError(data) {
    message('error', (data.title || '无法连接模型服务') + '\n' + (data.detail || '请检查模型配置。'));
    var wrap = el('thread').lastElementChild; var actions = document.createElement('div'); actions.className = 'message-actions';
    if (state.lastPrompt) {
      var resend = document.createElement('button'); resend.className = 'button primary'; resend.type = 'button'; resend.textContent = '重新发送';
      resend.addEventListener('click', function () { post({ action: 'send', prompt: state.lastPrompt, mode: state.mode, targetPath: state.targetPath }); });
      actions.appendChild(resend);
    }
    var configure = document.createElement('button'); configure.className = 'button primary'; configure.type = 'button'; configure.textContent = '配置模型'; configure.addEventListener('click', openSettings);
    var test = document.createElement('button'); test.className = 'button'; test.type = 'button'; test.textContent = '测试连接'; test.addEventListener('click', function () { openSettings(); post(connectionPayload('test_connection')); });
    actions.append(configure, test); wrap.appendChild(actions);
  }

  function scrollThread() {
    requestAnimationFrame(function () { el('threadScroll').scrollTop = el('threadScroll').scrollHeight; });
  }

  function escapeHtml(value) {
    return String(value || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function inlineMarkdown(value) {
    var text = escapeHtml(value);
    text = text.replace(/\x60([^\x60]+)\x60/g, '<code>$1</code>');
    text = text.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    text = text.replace(/__([^_]+)__/g, '<strong>$1</strong>');
    text = text.replace(/~~([^~]+)~~/g, '<del>$1</del>');
    text = text.replace(/(^|[^\*])\*([^*\n]+)\*/g, '$1<em>$2</em>');
    text = text.replace(/\[(S\d+)\]/g, '<span class="source-ref">[$1]</span>');
    return text;
  }

  function renderMarkdown(value) {
    var source = String(value || '').replace(/\\([#*_>\[\]~|\x60])/g, '$1').replace(/\r\n?/g, '\n');
    var lines = source.split('\n');
    var html = [];
    var paragraph = [];
    var listType = '';
    var code = [];
    var inCode = false;
    function flushParagraph() {
      if (!paragraph.length) return;
      html.push('<p>' + inlineMarkdown(paragraph.join(' ')) + '</p>');
      paragraph = [];
    }
    function closeList() {
      if (!listType) return;
      html.push('</' + listType + '>');
      listType = '';
    }
    for (var lineIndex = 0; lineIndex < lines.length; lineIndex += 1) {
      var line = lines[lineIndex];
      if (/^\s*\x60\x60\x60/.test(line)) {
        flushParagraph(); closeList();
        if (inCode) {
          html.push('<pre><code>' + escapeHtml(code.join('\n')) + '</code></pre>');
          code = []; inCode = false;
        } else {
          inCode = true;
        }
        continue;
      }
      if (inCode) { code.push(line); continue; }
      if (!line.trim()) { flushParagraph(); closeList(); continue; }
      if (line.includes('|') && lineIndex + 1 < lines.length && /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$/.test(lines[lineIndex + 1])) {
        flushParagraph(); closeList();
        var splitRow = function (row) { return row.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(function (cell) { return cell.trim(); }); };
        var headers = splitRow(line); lineIndex += 2;
        html.push('<table><thead><tr>' + headers.map(function (cell) { return '<th>' + inlineMarkdown(cell) + '</th>'; }).join('') + '</tr></thead><tbody>');
        while (lineIndex < lines.length && lines[lineIndex].includes('|') && lines[lineIndex].trim()) {
          var cells = splitRow(lines[lineIndex]);
          html.push('<tr>' + headers.map(function (_, index) { return '<td>' + inlineMarkdown(cells[index] || '') + '</td>'; }).join('') + '</tr>');
          lineIndex += 1;
        }
        html.push('</tbody></table>');
        lineIndex -= 1;
        continue;
      }
      var heading = /^(#{1,6})\s+(.+)$/.exec(line);
      if (heading) {
        flushParagraph(); closeList();
        var level = Math.min(4, heading[1].length);
        html.push('<h' + level + '>' + inlineMarkdown(heading[2]) + '</h' + level + '>');
        continue;
      }
      var quote = /^>\s?(.*)$/.exec(line);
      if (quote) { flushParagraph(); closeList(); html.push('<blockquote>' + inlineMarkdown(quote[1]) + '</blockquote>'); continue; }
      var unordered = /^\s*[-+*]\s+(.+)$/.exec(line);
      var ordered = /^\s*\d+[.)]\s+(.+)$/.exec(line);
      if (unordered || ordered) {
        flushParagraph();
        var nextType = unordered ? 'ul' : 'ol';
        if (listType !== nextType) { closeList(); listType = nextType; html.push('<' + listType + '>'); }
        html.push('<li>' + inlineMarkdown((unordered || ordered)[1]) + '</li>');
        continue;
      }
      if (/^\s*(---+|\*\*\*+)\s*$/.test(line)) { flushParagraph(); closeList(); html.push('<hr>'); continue; }
      paragraph.push(line.trim());
    }
    flushParagraph(); closeList();
    if (inCode) html.push('<pre><code>' + escapeHtml(code.join('\n')) + '</code></pre>');
    return html.join('');
  }

  function message(role, content, options) {
    options = options || {};
    var wrap = document.createElement('article');
    wrap.className = 'message ' + role;
    var head = document.createElement('div'); head.className = 'message-head';
    var roleEl = document.createElement('div'); roleEl.className = 'message-role'; roleEl.textContent = role === 'user' ? '你' : role === 'error' ? '未完成' : 'MarkLeaf Agent';
    var timeEl = document.createElement('time'); timeEl.className = 'message-time'; timeEl.textContent = now();
    head.append(roleEl, timeEl);
    var body = document.createElement('div'); body.className = 'message-body';
    if (role === 'agent') { body.classList.add('markdown-body'); body.innerHTML = renderMarkdown(content); }
    else body.textContent = content || '';
    wrap.append(head, body);
    if (options.sources && options.sources.length) {
      var sources = document.createElement('div'); sources.className = 'source-row';
      options.sources.forEach(function (source) {
        var chip = document.createElement('button'); chip.type = 'button'; chip.className = 'source-chip';
        chip.textContent = source.label || source.id || '来源'; chip.title = source.label || '';
        chip.disabled = !source.path;
        if (source.path) chip.addEventListener('click', function () { post({ action: 'open_source', path: source.path, line: source.line || 1 }); });
        sources.appendChild(chip);
      });
      wrap.appendChild(sources);
      body.querySelectorAll('.source-ref').forEach(function (reference) {
        var id = reference.textContent.replace(/[\[\]]/g, '');
        var source = options.sources.find(function (item) { return item.id === id; });
        if (!source || !source.path) return;
        reference.setAttribute('role', 'button'); reference.tabIndex = 0; reference.title = '打开 ' + source.label;
        var open = function () { post({ action: 'open_source', path: source.path, line: source.line || 1 }); };
        reference.addEventListener('click', open);
        reference.addEventListener('keydown', function (event) { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); open(); } });
      });
    }
    if (options.canApply) {
      var actions = document.createElement('div'); actions.className = 'message-actions';
      var apply = document.createElement('button'); apply.className = 'button primary apply-action'; apply.type = 'button'; apply.textContent = options.applyLabel || '应用章节修改';
      apply.addEventListener('click', function () {
        if (apply.dataset.confirmed === 'true') { post({ action: 'apply' }); return; }
        apply.dataset.confirmed = 'true'; apply.textContent = '确认写入';
        var cancel = document.createElement('button'); cancel.className = 'button apply-cancel'; cancel.type = 'button'; cancel.textContent = '取消';
        cancel.addEventListener('click', function () { apply.dataset.confirmed = 'false'; apply.textContent = options.applyLabel || '应用章节修改'; cancel.remove(); });
        actions.appendChild(cancel);
      });
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
    el('modeSelect').disabled = busy;
    el('targetDocument').disabled = busy;
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
      if (data.onClick) {
        row.classList.add('clickable'); row.tabIndex = 0; row.setAttribute('role', 'button');
        row.addEventListener('click', data.onClick);
        row.addEventListener('keydown', function (event) { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); data.onClick(); } });
      }
      row.append(copy, status); target.appendChild(row);
    });
  }

  function renderState(data) {
    el('contextName').textContent = data.documentName || data.projectName || '打开文档或文件夹后开始';
    state.model = data.model || '';
    state.providerName = data.providerName || '';
    state.providerType = data.providerType || 'openai-compatible';
    state.endpoint = data.endpoint || '';
    state.apiKeyConfigured = Boolean(data.apiKeyConfigured);
    state.confirmBeforeCloud = data.confirmBeforeCloud !== false;
    state.availableModels = data.availableModels || (state.model ? [state.model] : []);
    state.documents = data.documents || [];
    state.targetPath = '';
    var hasDocument = Boolean(data.documentName);
    var sourceCount = (data.sources || []).filter(function (item) { return item.ready; }).length;
    el('documentStep').classList.toggle('ready', hasDocument);
    el('documentStepTitle').textContent = hasDocument ? '已读取当前文档' : '等待主文档';
    el('documentStepDetail').textContent = hasDocument ? '已识别结构，可回答问题或生成修改预览' : '打开 Markdown 文档后识别结构和关键结论';
    el('documentStepStatus').textContent = hasDocument ? '完成' : '等待';
    el('sourceStep').classList.toggle('ready', sourceCount > 0);
    el('sourceStepTitle').textContent = sourceCount > 0 ? '已索引 ' + sourceCount + ' 份资料' : '尚未添加资料';
    el('sourceStepDetail').textContent = sourceCount > 0 ? '回答会标注文件、行号或 PDF 页码' : '打开项目文件夹后自动索引本地资料';
    el('sourceStepStatus').textContent = sourceCount > 0 ? '完成' : '等待';
    el('modelName').textContent = state.model || '配置模型';
    el('modelButton').title = (state.providerName ? state.providerName + ' · ' : '') + (state.model || '配置模型');
    renderModels(el('modelSearch').value);
    var target = el('targetDocument'); target.replaceChildren();
    if (!state.documents.length) {
      var emptyOption = document.createElement('option'); emptyOption.value = ''; emptyOption.textContent = data.documentName || '当前文档'; target.appendChild(emptyOption);
    } else {
      state.documents.forEach(function (item) { var option = document.createElement('option'); option.value = item.path; option.textContent = item.name; option.selected = Boolean(item.active); if (item.active) state.targetPath = item.path; target.appendChild(option); });
    }
    el('requirementsTitle').textContent = '任务要求（' + data.requirements.length + '）';
    el('sourcesTitle').textContent = '资料来源（' + data.sources.length + '）';
    el('auditsTitle').textContent = 'AI 与人工操作记录（' + (data.audits || []).length + '）';
    listRows(el('requirementsList'), data.requirements, '尚未明确导入任务要求；证据与来源检查仍可独立使用。', function (item) {
      var states = {
        covered: ['已覆盖', 'good'],
        partial: ['部分覆盖', 'warn'],
        missing: ['未覆盖', 'bad'],
        'quick-covered': ['快速匹配', 'warn'],
        'quick-missing': ['快速未发现', 'warn'],
        unchecked: ['待检查', '']
      };
      var presentation = states[item.coverageState] || states.unchecked;
      var locator = item.startLine ? '第 ' + item.startLine + (item.endLine && item.endLine !== item.startLine ? '-' + item.endLine : '') + ' 行' : '';
      var evidence = item.evidence ? '证据：' + item.evidence + (locator ? '（' + locator + '）' : '') : '';
      var detail = [item.description, item.reason, evidence].filter(Boolean).join(' · ');
      return {
        title: item.title,
        detail: detail,
        status: presentation[0],
        tone: presentation[1],
        onClick: item.path && item.startLine ? function () { post({ action: 'open_source', path: item.path, line: item.startLine }); } : null
      };
    });
    listRows(el('sourcesList'), data.sources, '尚未添加资料。当前文档会作为默认上下文。', function (item) { return { title: item.name, detail: item.kind + ' · ' + item.status, status: item.trust + '可信度', tone: item.ready ? 'good' : 'warn' }; });
    listRows(el('auditsList'), data.audits || [], '还没有 AI 写作或人工采纳记录。', function (item) { return { title: item.action, detail: item.time + ' · ' + item.detail, status: item.confirmed ? '人工确认' : 'AI 建议', tone: item.confirmed ? 'good' : '' }; });
    if (data.check) {
      el('checkDescription').textContent = data.check.verification === 'semantic'
        ? '语义核验已完成；每项结论必须绑定当前正文中的原句和行号。'
        : '当前为本地快速检查，只用于定位候选章节；点击“运行检查”可进行语义核验。';
      el('coverageValue').textContent = data.check.coverage + '%';
      el('evidenceValue').textContent = data.check.evidence + '%';
      el('issueValue').textContent = String(data.check.errors);
      listRows(el('issuesList'), data.check.issues, '没有发现需要处理的问题。', function (item) { return { title: item.title, detail: item.detail, status: item.state, tone: item.tone }; });
    } else {
      el('checkDescription').textContent = '集中查看要求覆盖、证据缺口和等待确认的章节修改。';
      el('coverageValue').textContent = '—'; el('evidenceValue').textContent = '—'; el('issueValue').textContent = '—';
      listRows(el('issuesList'), [], '打开文档后运行检查。', function () { return {}; });
    }
  }

  var toastTimer;
  function toast(text) { clearTimeout(toastTimer); el('toast').textContent = text; el('toast').classList.add('show'); toastTimer = setTimeout(function () { el('toast').classList.remove('show'); }, 2600); }

  function sendPrompt() {
    var value = el('prompt').value.trim();
    if (!value || state.busy) return;
    state.lastPrompt = value;
    switchPage('agent'); message('user', value); el('prompt').value = ''; post({ action: 'send', prompt: value, mode: state.mode, targetPath: state.targetPath || el('targetDocument').value });
  }

  document.querySelectorAll('.tab').forEach(function (tab) { tab.addEventListener('click', function () { switchPage(tab.dataset.page); }); });
  el('modeSelect').addEventListener('change', function () { state.mode = el('modeSelect').value; });
  el('targetDocument').addEventListener('change', function () { state.targetPath = el('targetDocument').value; if (state.targetPath) post({ action: 'select_target_document', path: state.targetPath }); });
  document.querySelectorAll('.suggestion').forEach(function (button) { button.addEventListener('click', function () { el('prompt').value = button.dataset.prompt; el('prompt').focus(); }); });
  el('send').addEventListener('click', function () { if (state.busy) post({ action: 'cancel' }); else sendPrompt(); });
  el('prompt').addEventListener('keydown', function (event) { if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); sendPrompt(); } });
  el('newTask').addEventListener('click', function () { el('thread').replaceChildren(); state.lastAssistant = null; el('prompt').focus(); });
  el('settings').addEventListener('click', openSettings);
  el('modelButton').addEventListener('click', function () { var open = !el('modelPicker').classList.contains('open'); closeModelPicker(); if (open) { el('modelPicker').classList.add('open'); el('modelButton').setAttribute('aria-expanded', 'true'); renderModels(el('modelSearch').value); setTimeout(function () { el('modelSearch').focus(); }, 20); } });
  el('modelSearch').addEventListener('input', function () { renderModels(el('modelSearch').value); });
  el('modelSearch').addEventListener('keydown', function (event) { if (event.key === 'ArrowDown') { var first = el('modelList').querySelector('.model-option'); if (first) { event.preventDefault(); first.focus(); } } if (event.key === 'Escape') closeModelPicker(); });
  el('modelList').addEventListener('keydown', function (event) { if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return; var options = Array.from(el('modelList').querySelectorAll('.model-option')); var index = options.indexOf(document.activeElement); if (index < 0) return; event.preventDefault(); var next = event.key === 'ArrowDown' ? Math.min(index + 1, options.length - 1) : Math.max(index - 1, 0); options[next].focus(); });
  el('addProvider').addEventListener('click', function () { openSettings(); applyProvider('openai-compatible', true); });
  el('manageModels').addEventListener('click', openSettings);
  document.querySelectorAll('.preset').forEach(function (button) { button.addEventListener('click', function () { applyProvider(button.dataset.provider, true); }); });
  el('closeSettings').addEventListener('click', closeSettings);
  el('cancelSettings').addEventListener('click', closeSettings);
  el('settingsModal').addEventListener('click', function (event) { if (event.target === el('settingsModal')) closeSettings(); });
  el('toggleKey').addEventListener('click', function () { var show = el('apiKey').type === 'password'; el('apiKey').type = show ? 'text' : 'password'; el('toggleKey').textContent = show ? '隐藏' : '显示'; });
  el('testConnection').addEventListener('click', function () { if (!el('endpoint').value.trim()) { connectionStatus('请先填写 API Base URL。', 'bad'); return; } connectionStatus('正在连接并读取模型列表…', ''); el('testConnection').disabled = true; post(connectionPayload('test_connection')); });
  el('saveSettings').addEventListener('click', function () { if (!el('endpoint').value.trim()) { connectionStatus('请填写 API Base URL。', 'bad'); return; } if (!el('modelInput').value.trim()) { connectionStatus('请填写模型 ID，或先测试连接。', 'bad'); return; } post(connectionPayload('save_settings')); });
  document.addEventListener('click', function (event) { if (!el('modelPicker').contains(event.target) && !el('modelButton').contains(event.target)) closeModelPicker(); });
  document.addEventListener('keydown', function (event) { if (event.key !== 'Escape') return; if (el('settingsModal').classList.contains('open')) closeSettings(); else closeModelPicker(); });
  el('importRequirements').addEventListener('click', function () { post({ action: 'import_requirements' }); });
  el('importSources').addEventListener('click', function () { post({ action: 'import_sources' }); });
  el('runCheck').addEventListener('click', function () { post({ action: 'run_check' }); });
  el('export').addEventListener('click', function () { post({ action: 'export' }); });
  el('exportPackage').addEventListener('click', function () { post({ action: 'export' }); });
  el('allowCloud').addEventListener('click', function () { el('cloudConsent').classList.remove('show'); post({ action: 'grant_cloud_consent' }); });
  el('denyCloud').addEventListener('click', function () { el('cloudConsent').classList.remove('show'); post({ action: 'deny_cloud_consent' }); toast('已取消，没有发送资料'); });

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', function (event) {
      var data = event.data || {};
      if (data.type === 'state') renderState(data);
      if (data.type === 'busy') setBusy(Boolean(data.busy), data.label || '正在处理', Boolean(data.cancellable));
      if (data.type === 'activity') activity(data.label || '正在处理', data.detail || '');
      if (data.type === 'assistant') { clearActivities(); message('agent', data.content, { sources: data.sources, canApply: data.canApply, applyLabel: data.applyLabel }); }
      if (data.type === 'error') { clearActivities(); message('error', data.message || '任务没有完成'); }
      if (data.type === 'cancelled') { clearActivities(); message('error', data.message || '任务已停止，没有修改文档。'); }
      if (data.type === 'connection_error') { clearActivities(); connectionError(data); }
      if (data.type === 'connection_testing') { el('testConnection').disabled = true; connectionStatus('正在连接并读取模型列表…', ''); }
      if (data.type === 'connection_result') {
        el('testConnection').disabled = false;
        connectionStatus(data.message || (data.success ? '连接成功。' : '连接失败。'), data.success ? 'good' : 'bad');
        if (data.success && data.models) {
          var datalist = el('discoveredModels'); datalist.replaceChildren();
          data.models.forEach(function (model) { var option = document.createElement('option'); option.value = model; datalist.appendChild(option); });
          if (!el('modelInput').value && data.models.length) el('modelInput').value = data.models[0];
        }
      }
      if (data.type === 'settings_saved') closeSettings();
      if (data.type === 'toast') toast(data.message || '已完成');
      if (data.type === 'semantic_check_failed') toast(data.message || '语义核验失败，已保留快速检查结果。');
      if (data.type === 'focus') { switchPage('agent'); el('prompt').focus(); }
      if (data.type === 'open_settings') openSettings();
      if (data.type === 'switch_page') switchPage(data.page || 'agent');
      if (data.type === 'open_model_picker') { switchPage('agent'); el('modelButton').click(); }
      if (data.type === 'cloud_consent_required') { clearActivities(); el('cloudConsentTitle').textContent = '允许 ' + (data.provider || '云端模型') + ' 处理资料'; el('cloudConsentText').textContent = data.message || ''; el('cloudConsent').classList.add('show'); }
      if (data.type === 'cloud_consent_resolved') el('cloudConsent').classList.remove('show');
      if (data.type === 'applied' && state.lastAssistant) { var button = state.lastAssistant.querySelector('.apply-action'); if (button) { button.disabled = true; button.textContent = '已应用，可撤销'; } var cancel = state.lastAssistant.querySelector('.apply-cancel'); if (cancel) cancel.remove(); }
    });
  }
  post({ action: 'ready' });
})();
</script>
</body>
</html>
""";
}
