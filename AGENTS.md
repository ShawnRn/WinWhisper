# Repository instructions

Read [workflows/README.md](workflows/README.md) for the development, native UI, model/media, subtitle/AI, verification and release procedures. Keep those workflows synchronized with implemented and validated behavior.

## Product and architecture

- The product is **WinWhisper**, an independent Windows 11 local media, transcription, subtitle and AI workspace. Do not copy code or resources out of the Memo installer.
- Retain the current `MemoAI.*` project/namespace identifiers and `%LOCALAPPDATA%/MemoAI.Native` data directory unless implementing an explicit, tested migration. App and owned background executable names / metadata use `WinWhisper` and `WinWhisper.Worker`; preserve upstream engine names and notices.

- Build a real Windows ARM64 and x64 WinUI 3 desktop application; do not replace the UI with a web application.
- Keep media, inference, and model downloads off the UI thread. Run native inference in separate processes.
- Default to local inference. Never silently upload media or transcripts to external AI services.
- Do not commit models, SDKs, native engine binaries, personal media, credentials, or generated outputs.
- Use pinned dependencies and record engine download sources and SHA-256 hashes.
- Keep progress in docs/PROGRESS.md honest; distinguish implemented behavior from runtime validation.
- Make local Git commits at coherent, verified milestones. Do not push without user instruction.

## Build and validation

- Read the relevant files before changing them. Use `scripts/bootstrap.ps1`, then `scripts/build.ps1 -Rid win-arm64` or `-Rid win-x64`; SDK and engine versions must remain pinned.
- Native workers and app output must match the selected RID. Check native PE architecture, while allowing managed AnyCPU assemblies. CUDA support depends on the actual engine build and self-test; do not infer acceleration from a GPU or driver version alone.
- Never edit XAML or app sources while a build is running. Wait for the process to finish before editing, launching its output, or copying it into a daily-use folder. Never replace a running executable.
- Use `scripts/test.ps1` for affected core behavior and `scripts/test-ui.ps1 -Rid <RID>` for native interaction changes. UI tests require an isolated `--data-root`; never clear personal media, transcripts, models or chat history for testing.
- Add `-PlaybackVideo <path>` when changing media/window lifetime; successful startup alone does not prove safe shutdown. Verify a loaded video can close with process exit code 0.
- Build both architectures for changes to shared UI or native integration. Record x64 execution under ARM64 emulation separately from validation on physical x64 hardware.
- Keep `docs/PROGRESS.md` and README accurate: distinguish implemented, tested, partially supported and planned behavior. Do not label a model as supported merely because a download link exists.

## Native UX and drawing

- Keep WinUI 3 and native accessibility, keyboard focus, IME and high-DPI behavior. Do not replace the app with a browser UI.
- Use one state surface per interactive item. Avoid nested hover/selection fills, square container overlays, delayed shape changes and full-tree rebuilding for a small state change.
- Cumulative transcription updates must reconcile subtitle IDs into stable objects and collections. Do not reset ListView ItemsSource or clear/rebuild lists on progress; preserve realized containers, selection, scroll, media and chat. Verify this with repeated copied Worker snapshots.
- G2 surfaces use edge-aligned tangents and zero endpoint curvature. Use the shared geometry, explicit antialiasing and DPI-aware sampling; do not claim that setting `CornerRadius` alone produces G2 continuity.
- Never scan the visual tree to overwrite `Background`, `CornerRadius` or theme bindings owned by native visual states. Establish drawing in the control template before the first visible frame; keep brush identity stable for solid-color state changes.
- Do not replace the `Clip` on XAML-owned composition visuals or reparent the XAML visual tree. Native controls rely on their named parts, CLR types and template structure.
- Native template adaptations are generated from the pinned SDK by `scripts/update-native-surfaces.ps1`; fix the generator and preserve transitive resources across Default, Light and HighContrast themes. Do not hand-edit generated output.
- Page, history and view-switch animations must respect Windows animation preferences and the app's reduced-motion option. Rapid navigation must reset old opacity/translation; replaying the current page must not flash it.
- Give asynchronous actions immediate, local busy/error/cancel feedback. Keep playback controls outside the picture, preserve reading position when the user scrolls up, and follow streaming text only when the user is near the bottom.
- Before claiming a visual fix, inspect the real native window as well as running appropriate regression checks. A screenshot is not proof of frame-by-frame flicker freedom.

## Models, data and text

- Model store entries must declare the actual engine, languages, architecture/backend constraints, source and license. Downloading and applying are separate validated steps; detect missing/incompatible files before starting inference.
- Keep model downloads explicit and show size/progress/cancel. Preserve resumable downloads and completed task stages. Never present a CPU-only runtime as CUDA-accelerated.
- Model and optional inference-runtime downloads belong in the model store and explicit first-run setup. Settings may configure parameters, services and existing paths, or reopen setup, but must not duplicate download controls.
- First-run setup uses an isolated, cancellable Worker for hardware / dependency checks and a bounded CPU DSP estimate. Do not report a generic CPU test or GPU memory capacity as measured ASR speed / verified GPU acceleration. Display pinned download sizes / sources, retain resume files, and commit model settings only after selected components finish. Save per-user completion or explicit skip; an interrupted setup must remain resumable.
- Keep first-run defaults simple: language, a recommended plan, download size and one preparation action. Put hardware, manual model / backend choices and dependency details in collapsed advanced options. Prefer the highest-quality compatible transcription model when memory, available capacity and compute estimates allow, without adding shared GPU memory to physical RAM.
- Chat conversations belong to one media session. Migrate legacy Chat / Summary exactly once, preserve independent history and drafts, block switching during generation / review, and never remove pending subtitle proposals when starting a conversation.
- GPU-intensive work is serialized by default. Keep decoding and inference outside the UI thread and isolate native crashes in workers.
- ONNX ASR models use versioned `.asr.json` installation records and the pinned catalog in `AsrModels.cs`. Never route an arbitrary `.onnx` file to Whisper, guess a model family, or apply a partially extracted model. Retain model-specific language and license limits.
- Live recording sends finalized PCM clips to a persistent worker. Keep offsets relative to the recording, preserve audio if inference fails, and retain the single GPU gate. COM process-loopback callbacks must be publicly COM-visible and agile; completion signals used by capture disposal must run continuations asynchronously.
- Media deletion must explicitly describe owned recording/cache removal. Preserve imported originals and shared references, reject path traversal and reparse points, and refuse deletion while recording/inference still writes that session. Track recording ownership even after associating another media file.
- Online media, browser Cookie extraction and third-party exports must be user-triggered. Do not log or commit Cookie values, tokens, authentication databases or credentials.
- Chinese output uses corner quotes 「」, title brackets 《》, spaces between Chinese and English/digits, and full-width Chinese commas. English contractions and possessives use ’, with ‘ ’ / “ ” for English quotations. Preserve code, URLs, file paths and valid JSON. Whisper uses decoding context rather than a chat system prompt; apply typography after transcription and in language-model prompts.
- Keep English apostrophes proportional through the Latin-first font fallback. Join recognised ASR contraction suffixes before sentence segmentation, retaining actual token time spans; do not join quoted words or cross line breaks.
- Never infer word alignment by dividing subtitle duration by character count. Preserve model token timestamps, validate their correspondence to the text, and use audio pauses only as boundary evidence. Punctuation restoration must reject paraphrases, dropped words, reordered words or changed numbers. Retain original cue boundaries when word timings are unavailable.
- Subtitle translation defaults to the full transcript and always exposes the chosen scope and target language. API translation requires an explicit configured provider and confirmation showing the destination and text scope; store keys only in Windows Credential Manager. Never route local chat through a translation API.
- Translate connected small batches with frozen original neighbours and validated, complete unique cue IDs. Commit only selected target translations, discarding reference translations; never feed generated translations back as source. Disclose neighbouring source outside the selected scope before API transmission. Recover VAD token times only from complete measured engine maps, never from cue-length or text ratios; do not silently rewrite existing transcripts or pending proposals.
- Media questions must inspect every transcript chunk. Never silently select the first N cues or truncate the transcript before answering, and never infer full-media absence from a partial excerpt. Show coverage progress for long transcripts.
- Transcript Agent capabilities are limited to paged reading, full-source search and validated edit proposals. Keep bounded tool loops, correlated events and cancellation; the model cannot apply revisions or access arbitrary files / shell. Preserve Proma attribution as a design reference, not a code dependency.
- Persist proposal baselines, parent versions, complete before/after text and decisions. Apply only through an explicit Diff review, checking both memory and database hashes in the same transaction as the revision decision. Rejecting never changes subtitles; rollback appends history. Do not create full versions for every intermediate ASR progress batch.
- Review Diff and complete before/after views inside the original transcript pane, keeping player and chat visible. Preserve pending proposals on later/navigation/close; do not apply them implicitly or replace review with a blocking modal.
- Diff highlighting applies only to changed words / graphemes. Keep unchanged context in the normal text color, preserve both source strings exactly and bound comparison memory for large imported cues.

## Open source and release hygiene

- Original application code is MIT, matching the owner's other original projects. Preserve upstream notices and licenses; the repository license does not relicense SDKs, engines, fonts or models.
- Update `CREDITS.md` when adding dependencies or using external code. Distinguish actual code reuse from interface/workflow references such as Proma and yt-dlp-gui.
- Treat SDK-derived generated templates as local build inputs under the SDK terms; do not publish them as original MIT source. Keep their generation reproducible and retain the SDK notices for distribution.
- Use the user-provided SVG as the formal icon source. Commit intentional brand assets, not temporary previews or build products; regenerate platform icons with `scripts/generate-icons.mjs`.
- Before publishing, inspect tracked files and history for credentials, personal paths/data, model weights and binaries. Do not add ignored artifact or engine directories to make a build appear complete.
- Package with the pinned portable NSIS compiler and `scripts/package.ps1`; keep Universal, x64 and ARM64 installers. Current-user installation is the default, all-users installation requires Windows elevation, and the optional desktop shortcut is unchecked.
- Use `scripts/test-installer.ps1` in an isolated, ownership-marked directory. Verify the installed native app, architecture selection, upgrade, shortcuts, registration cleanup and preservation of unrelated files/data. Never approve UAC or security dialogs through automation; use an already elevated CI environment for all-users QA.
- Large offline payloads are precompressed with a native encoder, checked by the pinned extractor, and embedded without recompression. Retain the helper's license/source and the total-payload size guard. Reused stage/payload directories must belong to the same verified build; record the actual encoder version in release provenance.
- Publish third-party notices, source access/build recipes and SHA-256 checksums alongside the installers. Keep release notes explicit about unsigned installers, compatibility testing and known model/backend failures. Only publish the prerelease after its packaged assets pass the required checks.
- Preserve published tags and artifact checksums. Record the app/Worker build commits, actual packaging tools, local UI coverage and hosted CI coverage in release provenance. Update README download links after verifying the release is public; later documentation updates must not rewrite the published tag.

