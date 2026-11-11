# Files (History Follow) — unofficial fork

This is an **unofficial** fork of [Files](https://github.com/files-community/Files). It is not made, endorsed or supported by the Files Community.

## What was added

履歴フォルダを自動で表示する機能を追加しました。

Adds a **History** section to the sidebar, below Pinned. It shows the folders you use most recently and most often as a compressed tree:

- File use is recorded from Files (open, move, copy, rename), from the Windows "Recent" folder and from the Word recent file list.
- The folders directly under a drive (or under the user profile) are the parents; folders between branch points are left out.
- A temporary diagnostics row explains why a folder is, or is not, listed.

New code lives in `src/Files.History` (UI independent, with tests in `tests/Files.History.Tests`), `src/Files.App/Services/History` and `src/Files.App/ViewModels/UserControls/SidebarViewModel.History.cs`. Small hooks were added to existing files; see the Git history of the `feature/folder-history` branch for the exact changes.

## Licenses

- Most of the code is MIT licensed: see [LICENSE-MIT](LICENSE-MIT).
- Some files carry an `SPDX-License-Identifier: MPL-2.0` header and remain under the Mozilla Public License 2.0: see [LICENSE-MPL](LICENSE-MPL). Source for every file, including the modified ones, is available in this repository.
- Third-party components and their licenses are listed in [.github/NOTICE.md](.github/NOTICE.md). Include that file when you redistribute a binary.
- The new code written for this fork is Copyright (c) koji inoguchi(nagasaki) and is MIT licensed.

## Name and branding

"Files" and its logo belong to the Files Community. This fork keeps the original package identity for compatibility, but its display name is "Files (History Follow)" and its publisher display name is the fork author's. Do not present builds of this fork as official Files releases.

## Telemetry

The upstream crash-reporting key is injected only by the upstream release workflow. Builds made from this repository contain just a placeholder, so they are not set up to report to the upstream project.

## No warranty

As stated in the MIT license, the software is provided "as is", without warranty of any kind.
