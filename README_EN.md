<div align="center">
  <img src="./assets/current/yuqi-logo.png" width="92" alt="Yuqi Engine Logo">

# Yuqi Engine

**A modular Windows 10/11 utility for system inspection, explainable optimization, diagnostics, backup, and recovery**

[简体中文](./README.md) · **English**

[![Release](https://img.shields.io/badge/release-v0.1.1-0f9f92?style=flat-square)](https://github.com/ZYY817/YuqiEngine/releases)
[![GitHub Release downloads](https://img.shields.io/github/downloads/ZYY817/YuqiEngine/total?style=flat-square&label=GitHub%20Release%20downloads&color=22c55e)](https://github.com/ZYY817/YuqiEngine/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-1677ff?style=flat-square)](#system-requirements)
[![Language](https://img.shields.io/badge/UI-中文%20%2F%20English-7c4dff?style=flat-square)](./README.md)
[![Stage](https://img.shields.io/badge/stage-public%20preview-f59e0b?style=flat-square)](#preview-status)

[Download](#download) · [Screenshots](#screenshots) · [Read before use](#read-before-use) · [Report a problem](#report-a-problem) · [简体中文](./README.md)

<img src="./assets/current/startup-current.gif" width="800" alt="Yuqi Engine v0.1.1 startup animation">

<sub>This animation was rendered from the current v0.1.1 startup page. It is not a concept mockup.</sub>
</div>

---

## Preview status

> [!IMPORTANT]
> **Yuqi Engine is currently a public preview. It is not a final product validated on every device and configuration.**

> [!WARNING]
> **Complete your backups before applying any system change.** Undiscovered problems may still exist, and no optimization is guaranteed to suit your PC or improve performance. Create both a Yuqi Engine backup and a Windows restore point, and separately back up important personal files. If you cannot accept the possibility of restoring settings, using a restore point, or reinstalling Windows in an extreme case, use inspection features only and do not apply system changes.

Yuqi Engine is developed and maintained by one independent developer. Available test hardware cannot cover every CPU, GPU, driver, Windows build, regional policy, security product, and third-party software combination. A feature may be unavailable, partially confirmed, ineffective, or incompatible on a particular computer even after automated and hands-on testing.

Reported and reproducible problems will be investigated and improved over time. However, Yuqi Engine does not promise that every feature will work on every PC, and it does not promise a fixed FPS, latency, or performance gain.

| Current information | Details |
|---|---|
| Latest preview | `v0.1.1` |
| Supported systems | Windows 10 1809 (Build 17763) or later; Windows 11; x64 only |
| Packages | Bilingual installer and portable ZIP |
| Runtime | Self-contained .NET 8 with an offline WebView2 installer |
| Code signing | The preview is currently unsigned and Windows may show Unknown Publisher or SmartScreen warnings |
| Product scope | Inspect, explain, select, apply, verify, and restore by module; not a universal one-click speed booster |

## What it does

Yuqi Engine organizes Windows optimization, diagnostics, and recovery tools into one interface. It attempts to read real system state before telling you:

- what the current setting is;
- whether the item applies to the current computer;
- why it is unavailable or requires administrator privileges;
- what the change may affect and how it can be restored;
- whether the operation was verified or still needs another read or manual confirmation.

A failed inspection is not reported as success. Inspection-only actions do not silently modify system settings.

## Main capabilities

| Area | Included capabilities |
|---|---|
| Gaming and performance | Game Optimization Center, performance tuning, CPU tuning, graphics and display, memory, audio, network, and peripherals |
| Windows settings | Privacy, system tuning, desktop experience, scheduled tasks, services, and cleanup |
| Hardware and diagnostics | Hardware information, GPU model tools, driver reference, display-mode inspection, game-security diagnostics, and network tests |
| Recovery and maintenance | App backups, Windows restore-point entry, Repair Center, configuration packages, startup management, and power-plan tools |
| Process tools | CPU affinity, priority, and core scheduling for explicitly selected processes with the required privileges |
| Persistent guard | A lightweight helper that maintains only previously applied items that support safe automatic restoration |
| Explainable results | Partial reads, missing privileges, unsupported platforms, interrupted checks, and unverified applications are shown as different states |

Feature count is not a performance guarantee. The number of readable, applicable, or guard-eligible items varies by computer.

## Download

Download the latest preview from **[GitHub Releases](https://github.com/ZYY817/YuqiEngine/releases)**. Users in mainland China may also use the author's **[Quark Drive mirror](https://pan.quark.cn/s/432600ae9d8d)**.

> The download badge counts cumulative file downloads from GitHub Releases only. It excludes the Quark Drive mirror and does not represent installations, active users, or unique users.

| Package | Recommended for | How to use |
|---|---|---|
| Installer (Chinese/English) | Users who want Start menu and desktop shortcuts | Select Chinese or English in the installer, then follow the setup wizard |
| Portable ZIP (Chinese/English) | Testing or portable use | Extract the entire archive, then run `YuqiEngine.exe`; do not run it inside the ZIP |

Both packages contain the same product features. Some system-level changes require administrator privileges, while read-only and inspection features remain available without elevation where supported.

### Why the package is large

The current package is approximately 530 MB because it includes the self-contained .NET 8 runtime, an offline WebView2 installer, bilingual frontend resources, full feature modules, backup/recovery components, diagnostics, and the lightweight persistent guard. A smaller package may be evaluated later without sacrificing offline reliability.

### Verify a download

Each release should include SHA-256 checksums. Open PowerShell in the download directory and run:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '.\downloaded-file.exe'
```

Use the `.zip` filename for a ZIP package. The result must exactly match the checksum published for that release. A checksum verifies file integrity but does not replace code signing.

## System requirements

| Item | Requirement |
|---|---|
| Operating system | Windows 10 1809 (Build 17763) or later / Windows 11 |
| Architecture | x64 |
| Memory | 4 GB minimum; 8 GB or more recommended |
| Storage | At least 1 GB recommended for the app, updates, logs, and backups |
| WebView2 | Offline installer included; the app guides installation if required |
| Privileges | Inspection is usually read-only; modifying services, Registry settings, power configuration, or other system settings may require administrator privileges |

## Read before use

1. **Read the item description first.** Review the purpose, risk, target location, current value, and recovery method.
2. **Create backups first.** Create both a Yuqi Engine backup and a Windows restore point.
3. **Apply a small number of changes and retest.** Do not enable many unfamiliar settings at once. Test your real games and workloads.
4. **Keep only changes that genuinely help.** Restore an item when it does not help, creates compatibility problems, or cannot be verified.

> [!WARNING]
> Yuqi Engine backups preserve app-related settings, Registry entries, and recovery evidence. **They are not full system images and do not back up personal documents, photos, videos, or game saves.** Back up important personal data separately. A Windows restore point or Windows reinstallation is not a substitute for personal-data backup.

### Understanding status labels

| Status | Meaning |
|---|---|
| Confirmed | The current system state was read successfully and can be evaluated |
| Reading | Windows is still returning state and counts may change |
| Unconfirmed | No reliable verification result is available; do not treat the item as applied |
| Unavailable | Privileges, Windows version, hardware, dependencies, or other conditions are not satisfied; details explain why |
| Applied / Restored | The operation returned a result; follow the page instructions to recheck or read the state again |
| Partially completed | Only some items completed; handle the remaining entries individually |

## Screenshots

### System Overview

<img src="./assets/current/overview-v0.1.1.png" width="100%" alt="Yuqi Engine v0.1.1 System Overview">

<details>
<summary><strong>View Game Optimization Center</strong></summary>

<br>
<img src="./assets/current/game-optimization-v0.1.1.png" width="100%" alt="Yuqi Engine v0.1.1 Game Optimization Center">
</details>

<details>
<summary><strong>View Persistent Guard Assistant</strong></summary>

<br>
<img src="./assets/current/guard-assistant-v0.1.1.png" width="600" alt="Yuqi Engine v0.1.1 Persistent Guard Assistant">
</details>

Screenshots come from the current v0.1.1 application. Statuses and counts depend on the test machine and do not represent guaranteed results on every PC.

## Safety, recovery, and limits

- The app distinguishes inspection, selection, application, verification, and restoration; submitted does not mean verified.
- Missing privileges, Windows policy, drivers, hardware differences, or security software may make some features unavailable.
- A change may improve responsiveness, frame pacing, or background interference on some PCs, have no visible effect, or create a compatibility issue.
- The persistent guard handles only applied items that support safe restoration and pass eligibility checks. It does not continuously force every optimization setting.
- Restoration rereads the current state first. The app does not claim success when it cannot verify the result.
- If a problem occurs, first use the Yuqi Engine backup or Windows restore point. Reinstalling Windows should be a last resort and requires a separate personal-data backup.
- Preview builds are currently unsigned. Download only from this repository's Releases, the author's published mirror, or the author's official accounts, and verify SHA-256.

## Report a problem

If you encounter an unapplied item, partial state read, page failure, or a feature unavailable on a specific device:

### What is an issue package?

**An issue package is a troubleshooting ZIP created locally by Yuqi Engine. It helps the author understand which environment, module, and error were involved. It is not an optimization backup or a system image.**

Depending on what is available on the computer, it may include:

- Yuqi Engine version, Windows version, architecture, and basic hardware environment;
- application, page, and startup diagnostic logs;
- recent optimization, recovery, and error records;
- sanitized application configuration;
- application crash information and readable Windows evidence for crashes, black screens, or unexpected restarts.

Usernames, machine names, paths, and sensitive log fields are redacted. The ZIP is **created locally only and is never uploaded or sent automatically**. The collector does not intentionally include personal documents, photos, videos, or game saves. Review the ZIP before sharing it and never include passwords, tokens, chats, or other private material.

After creation, Yuqi Engine shows the package location. Send the ZIP together with screenshots and reproduction steps through GitHub Issues, Douyin direct message, or Bilibili direct message.

1. Open **Backup Management** in Yuqi Engine.
2. Select **Generate issue package**.
3. Include a screenshot, the affected module, reproduction steps, Windows version, CPU/GPU, and the generated package.
4. Submit it through [GitHub Issues](https://github.com/ZYY817/YuqiEngine/issues), Douyin direct message, or Bilibili direct message.

The issue package summarizes diagnostic logs, configuration, crash information, and recent optimization/recovery records. It should not include personal documents. Review the archive before sending it, and never include passwords, tokens, or other private data.

## FAQ

<details>
<summary><strong>Does Yuqi Engine guarantee higher FPS?</strong></summary>

No. Results depend on hardware, cooling, drivers, Windows build, games, and existing settings. Retest your real workload and keep only changes that genuinely help.
</details>

<details>
<summary><strong>Why is an item unconfirmed or unavailable?</strong></summary>

The app either did not obtain a reliable state or the current device, privileges, Windows version, or dependencies do not satisfy the requirements. Open Details or use Read again for the specific reason. Unconfirmed items are excluded from application.
</details>

<details>
<summary><strong>Why does Windows show Unknown Publisher?</strong></summary>

The current preview is released by an independent developer who has not yet purchased a trusted commercial code-signing certificate. Download only from official channels and verify the SHA-256 checksum published with the release.
</details>

<details>
<summary><strong>Should I disable antivirus software when it reports a warning?</strong></summary>

No. Do not disable security software or add an unconditional exclusion. First verify the download source and checksum. If the file remains blocked, report the detection name, file hash, app version, and issue package for investigation.
</details>

<details>
<summary><strong>Does the persistent guard maintain every optimization?</strong></summary>

No. Only eligible items that were already applied, support safe automatic restoration, and were enabled by the user enter the guard. Other items are excluded.
</details>

## Roadmap

- Expand validation across more CPU, GPU, Windows, and driver combinations.
- Continue fixing state-reading, compatibility, and recovery paths using reproducible issue packages.
- Improve documentation, bilingual text, and accessibility.
- Add trusted code signing when practical.
- Evaluate a smaller online package while keeping the complete offline release.

Priorities depend on reproducible problems, risk, and user feedback. No fixed release date is promised.

## Repository and source-code status

This public repository currently provides official information, downloads, release notes, and issue reporting. **Public visibility does not mean that the current application source code has been released, and it does not grant permission to copy, modify, repackage, or commercially use the software.**

If source code, tools, or APIs are published later, their directories will include an explicit license and scope. Do not assume open-source permission when no license is provided.

## Author and official channels

The names differ by platform but refer to the same author:

- **Douyin: 智宇** (Douyin ID `35071658`) — [Profile](https://www.douyin.com/user/MS4wLjABAAAAtudWpqXWwZXYdkSk0YT36zpHEPiS2YuFsQL21ihiUxI)
- **Bilibili: 智宇丶817** (UID `41173761`) — [Profile](https://space.bilibili.com/41173761)
- **GitHub: ZYY817** — [Repository](https://github.com/ZYY817/YuqiEngine)
- **Quark Drive mirror** — [Author-published link](https://pan.quark.cn/s/432600ae9d8d)

Yuqi Engine is permanently free and may not be resold. Redistribution, re-uploading to another cloud drive, repackaging, bundling unrelated content, distributing modified builds, or using the software for traffic monetization requires explicit permission from the author. Do not impersonate the author or an official release.

---

<div align="center">
  <strong>Yuqi Engine</strong><br>
  <sub>Understand first, then choose. Verify changes and keep recovery available.</sub>
</div>
