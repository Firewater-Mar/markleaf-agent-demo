# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

The primary users are university students preparing Markdown coursework, reports, technical notes, and resumes for A4 printing or paper reading. The project is also evaluated as a university software-innovation competition entry.

## Product Purpose

MarkLeaf is a lightweight, offline-first Markdown editor that makes print typography inspectable and correctable. Its document workflow Agent understands a user's layout intent, plans bounded document-tool calls, runs local assessment and typography tools, and presents every proposed content change for review.

## Positioning

Unlike a general writing assistant, MarkLeaf combines Markdown AST analysis with measured WebView2 DOM geometry to optimize A4 typography without generating or rewriting document prose.

## Operating Context

Users edit Markdown in a native Windows WinForms application backed by WebView2, switch into an AI workspace when they want natural-language assistance, inspect the Agent's plan and tool activity, and approve or reject document diffs before anything is applied. Evaluation, optimization, templates, import, export, and print workflows remain usable without a network connection.

## Capabilities and Constraints

- Stack: C# WinForms, WebView2, shared TypeScript editor core.
- The Agent is limited to Markdown, typography, A4 printing, templates, document import/export, and explanation of local assessment results.
- AI providers are optional and loaded on demand. Pure mode must not load Agent implementation code or access the network.
- The model may interpret intent and propose a bounded plan; local code owns task scheduling, document mutation, scoring, termination, and diff generation.
- The Agent must not generate正文, become a general computer agent, run arbitrary shell commands, or silently overwrite a document.
- API credentials are stored through Windows Credential Manager rather than ordinary settings files.
- The upstream `zhuanshunjishi2017/markleaf` repository belongs to another author and must never be modified or pushed to by this project.

## Brand Commitments

The product name is MarkLeaf. The experience should remain lightweight, calm, document-first, and explicit about what is local, what is sent to an AI provider, and what still requires user approval.

## Evidence on Hand

- Existing Windows implementation: `apps/windows/MarkLeaf`.
- Shared Markdown editor core: `packages/editor-core`.
- Existing P0 typography evaluator, planner, executor, feedback loop, and diff review under `apps/windows/MarkLeaf/Services/Typography`.
- Optional OpenAI-compatible generation provider under `apps/windows/MarkLeaf.AI.OpenAICompatible`; it remains lazy-loaded, while deterministic task routing stays local and offline.
- No competition awards, user testimonials, or external performance claims have been supplied; future surfaces must not fabricate them.

## Product Principles

1. Show the Agent thinking through visible plans and tool results, not through decorative AI branding.
2. Keep document changes deterministic, local, reversible, and individually reviewable.
3. Preserve full offline value; network AI is an enhancement rather than a dependency.
4. Keep the Agent's authority narrower than the user's document and narrower than a general writing assistant.
5. Optimize for A4 print readability while preserving the author's original prose.

## Accessibility & Inclusion

The Windows interface must remain keyboard operable, usable with Windows DPI scaling, readable at common desktop widths, and must not communicate state through color alone.
