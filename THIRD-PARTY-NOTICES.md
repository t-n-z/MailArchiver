# Third-Party Notices

MailArchiver's own code is MIT-licensed (see `LICENSE`). It also includes vendored source
and references NuGet packages, each under its own license. All permit commercial use; none
impose copyleft on MailArchiver's own code. The notices below satisfy the attribution
requirements.

---

## 1. XstReader — vendored, **patched** (Ms-PL)

`src/XstReader.Api/` is vendored from:

- Project: https://github.com/iluvadev/XstReader
- Commit: `da80989623f60685beaf5132948d32a3da48fd60`
- Copyright (c) 2016, Dijji; (c) 2021, iluvadev. Based on the original work of Dijji
  (https://github.com/dijji/XstReader).
- License: **Microsoft Public License (Ms-PL)** — full text in section 5 below.

**Local patch:** `src/XstReader.Api/NDB.cs` — `ReadAndDecompress` (the `IsUnicode4K` branch)
was modified to bound the `DeflateStream` to the compressed block's actual length. Upstream
wraps the raw file stream with no limit, which makes 4K-format `.ost` files decompress far
past each block (minutes per message). The change is marked `LOCAL PATCH` in the file. This
vendored copy therefore diverges from upstream.

## 2. MsgKit — vendored (MIT)

`src/XstReader.MsgKit/` is the MsgKit subtree, vendored from the same XstReader commit. MsgKit
originates from:

- Project: https://github.com/Sicos1977/MsgKit
- Copyright (c) Kees van Spelde / Magic-Sessions.
- License: **MIT** — full text in section 6 below. (Each file in `src/XstReader.MsgKit/`
  also carries its own header.)

`XstReader.MsgKit` depends on **OpenMcdf** (see section 4).

## 3. NuGet packages

| Package | Used by | License |
|---------|---------|---------|
| OpenMcdf 2.2.1.12 | XstReader.MsgKit | MPL-2.0 |
| MimeKit 4.16.0 | MailArchiver.Mime, Viewer | MIT |
| BouncyCastle.Cryptography | (MimeKit dependency) | MIT |
| MSGReader 6.0.11 | Viewer | MIT |
| Microsoft.Data.Sqlite 9.0.0 | MailArchiver.Core | MIT |
| System.Text.Encoding.CodePages 9.0.0 | Viewer | MIT |
| Microsoft.CSharp, System.Security.Cryptography.Pkcs | XstReader.Api | MIT |
| .NET 9 runtime / SDK | all projects | MIT |

**OpenMcdf** is the only non-MIT/non-Ms-PL dependency. It is MPL-2.0 and is used as an
**unmodified** binary (no OpenMcdf source file is edited), so MPL-2.0 imposes no obligation
on MailArchiver's own code — only that this notice and a pointer to the source exist:
https://github.com/ironfede/openmcdf . Full license texts for all NuGet packages are in
their respective `.nupkg` files / project repositories.

---

## 5. Microsoft Public License (Ms-PL) — applies to XstReader (section 1)

This license governs use of the accompanying software. If you use the software, you accept this license. If you do not accept the license, do not use the software.

1. Definitions

The terms "reproduce," "reproduction," "derivative works," and "distribution" have the same meaning here as under U.S. copyright law.

A "contribution" is the original software, or any additions or changes to the software.

A "contributor" is any person that distributes its contribution under this license.

"Licensed patents" are a contributor's patent claims that read directly on its contribution.

2. Grant of Rights

(A) Copyright Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free copyright license to reproduce its contribution, prepare derivative works of its contribution, and distribute its contribution or any derivative works that you create.

(B) Patent Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free license under its licensed patents to make, have made, use, sell, offer for sale, import, and/or otherwise dispose of its contribution in the software or derivative works of the contribution in the software.

3. Conditions and Limitations

(A) No Trademark License- This license does not grant you rights to use any contributors' name, logo, or trademarks.

(B) If you bring a patent claim against any contributor over patents that you claim are infringed by the software, your patent license from such contributor to the software ends automatically.

(C) If you distribute any portion of the software, you must retain all copyright, patent, trademark, and attribution notices that are present in the software.

(D) If you distribute any portion of the software in source code form, you may do so only under this license by including a complete copy of this license with your distribution. If you distribute any portion of the software in compiled or object code form, you may only do so under a license that complies with this license.

(E) The software is licensed "as-is." You bear the risk of using it. The contributors give no express warranties, guarantees or conditions. You may have additional consumer rights under your local laws which this license cannot change. To the extent permitted under your local laws, the contributors exclude the implied warranties of merchantability, fitness for a particular purpose and non-infringement.

---

## 6. MIT License — applies to MsgKit (section 2)

Copyright (c) Kees van Spelde (Magic-Sessions)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
