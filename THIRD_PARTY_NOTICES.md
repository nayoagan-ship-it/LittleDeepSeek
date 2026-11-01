# Third-party notices

Original project code and the geometric placeholder generator are licensed under the root MIT LICENSE.
Third-party components retain their own copyright and license terms. The MIT license does not grant rights to DeepSeek trademarks or third-party character designs.

## Microsoft WebView2

The Windows build restores Microsoft.Web.WebView2 1.0.4129.50 from NuGet.
The WebView2 SDK license and bundled notices are reproduced in `licenses/WebView2/LICENSE.txt` and `licenses/WebView2/NOTICE.txt` and copied to build outputs.
Microsoft Edge WebView2 Runtime and .NET Framework are installed separately and remain subject to their own terms.

## Todo interface

The interface uses React, React DOM, Base UI, Lucide, shadcn UI components and styles, class-variance-authority, clsx, tailwind-merge and related build dependencies.
Exact versions and integrity hashes are recorded in `todo-web/package-lock.json`.
Dependency license notices are included in `licenses/npm/` with an index in `licenses/npm/INDEX.md`.
Those notices also accompany the prebuilt JavaScript and CSS in `todo-app/`.

## Character art

No original character images, icons, animation clips, source video, or previous executable are distributed in this source release.
The original demo used DSH-related character material. The upstream dsh-pet project distinguishes its MIT code from non-commercial character/animation material:
https://github.com/PC2005-cloud/dsh-pet#许可

That material is not relicensed by this project. Development builds use only the geometric placeholders produced by `source/make-placeholder-assets.ps1` unless the builder supplies replacement assets. See ASSETS.md.

## Optional integrations

DeepSeek Harness is a separate user-installed program; its code, account configuration, credentials and runtime data are not bundled here.
The weather integration can query wttr.in on demand. No weather dataset is bundled.

## Campus map service

The optional hosted campus map uses AMap and DeepSeek APIs under the maintainer's runtime configuration. No private provider credentials or local database are distributed.
Campus point data comes from NJAU-Map; its MIT license is retained in `campus-service/LICENSE-NJAU-Map.txt`. SDK/map tiles are loaded from AMap at runtime and are not relicensed as project source.
