# MarkLeaf Agent Design System

## Product Surface

MarkLeaf Agent is an Operate-mode Windows desktop workspace. The first screen always shows the Markdown editor and the Agent together; there is no separate Agent entry screen.

## Layout

- Left: project files, search, and document outline.
- Center: the full MarkLeaf Markdown editor and preview surface.
- Right: a persistent, resizable Agent workbench with Task, Context, and Check views.
- At narrow Agent widths, suggestion grids and metrics become single-column instead of compressing text.
- The composer remains visible at the bottom while the task thread scrolls independently.

## Tokens

| Role | Value |
| --- | --- |
| Canvas | `#F7F8F7` |
| Surface | `#FFFFFF` |
| Secondary surface | `#F1F4F2` |
| Primary text | `#16211D` |
| Secondary text | `#5D6B65` |
| Border | `#DCE3DF` |
| MarkLeaf accent | `#176B55` |
| Accent soft | `#E4F1EC` |
| Danger | `#B3433E` |
| Warning | `#A76520` |
| Radius | `9px` controls, `12px` content, `14px` composer |
| Motion | `160–180ms`, state changes only |

Typography uses the Windows system stack: Segoe UI, Microsoft YaHei UI, Microsoft YaHei, sans-serif. Body copy is 13–14px with 1.5–1.65 line height. Headings use weight before color for hierarchy.

## Component Language

- One green accent is reserved for the current view, primary actions, focus, and active progress.
- Cards are used only for actionable suggestions or bounded results; normal information uses rows and dividers.
- Icons are inline stroke SVG with a consistent 1.7–1.9 stroke. Emoji are not structural icons.
- Every action has hover, focus, pressed, disabled, loading, success, and error behavior where applicable.
- Agent activity is a readable timeline, not a decorative progress dashboard.

## Agent Interaction Contract

- Plan mode analyzes and proposes steps without producing insert-ready prose.
- Execute mode produces reviewable Markdown and exposes an explicit Apply action.
- Sources appear beside the answer and are converted to portable footnotes when applied.
- Remote transmission and document writes retain confirmation boundaries.
- Running model calls can be stopped. Applied text remains undoable in the editor.
- Errors state what failed; empty states teach the next useful action.

## Accessibility and Responsiveness

- Visible keyboard focus uses a 2px green ring.
- Icon-only controls have accessible names; active tabs and modes expose selected/pressed state.
- Text and status labels supplement color.
- Reduced-motion preferences collapse animations to near-instant state changes.
- Long paths, titles, and generated content wrap or truncate intentionally without horizontal scrolling.
- The workbench remains usable from 320 logical pixels upward and is verified at normal and high DPI.

## Anti-Patterns

Do not introduce AI-purple gradients, glassmorphism, emoji navigation, nested cards, decorative progress rings, generic chat bubbles, or direct unreviewed document replacement.
