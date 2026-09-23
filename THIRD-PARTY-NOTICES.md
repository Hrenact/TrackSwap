# Third-Party Notices

TrackSwap includes or interoperates with the following third-party software.

## Xelu Free Controller Prompts

Project: https://github.com/Haaldor/Xelu_prompts_SVG

The Xbox Series controller prompt artwork under
`src/TrackSwap/Assets/ThirdParty/Xelu/Xbox Series` originates from Nicolae
(Xelu) Berbece's controller prompt pack. The SVG extraction and exported image
collection were prepared by Haaldor. The included Xbox Series assets are made
available under Creative Commons CC0 1.0 Universal. TrackSwap preserves the
source package readme and the complete CC0 legal text beside the assets in
`src/TrackSwap/Assets/ThirdParty/Xelu`.

Only the Xbox Series subset is used here; the source package's separate warning
about the font used by its Steam Deck artwork does not apply to these files.

## Inno Setup

Project: https://jrsoftware.org/isinfo.php

TrackSwap's optional Windows installer is built with Inno Setup. Inno Setup is
not required to build or run the unpacked TrackSwap applications.

```text
Inno Setup License

Copyright (C) 1997-2026 Jordan Russell. All rights reserved.
Portions Copyright (C) 2000-2026 Martijn Laan. All rights reserved.

This software is provided "as-is," without any express or implied warranty. In
no event shall the author be held liable for any damages arising from the use
of this software.

Permission is granted to anyone to use this software for any purpose, including
commercial applications, and to alter and redistribute it, provided that the
following conditions are met:

1. All redistributions of source code files must retain all copyright notices
   that are currently in place, and this list of conditions without
   modification.
2. All redistributions in binary form must retain all occurrences of the above
   copyright notice and web site addresses that are currently in place (for
   example, in the About boxes).
3. The origin of this software must not be misrepresented; you must not claim
   that you wrote the original software. If you use this software to distribute
   a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
4. Modified versions in source or binary form must be plainly marked as such,
   and must not be misrepresented as being the original software.

Jordan Russell
https://jrsoftware.org/
```

## Newtonsoft.Json

Project: https://github.com/JamesNK/Newtonsoft.Json

TrackSwap distributes Newtonsoft.Json with its binary releases.

```text
The MIT License (MIT)

Copyright (c) 2007 James Newton-King

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## OpenVR

Project: https://github.com/ValveSoftware/openvr

TrackSwap uses OpenVR API definitions, builds the official
`handskeletonsimulation` helper into its native driver, and dynamically loads
the OpenVR library from the user's local SteamVR installation. The native
driver build and runtime bindings use the `IVRSystem_026` and
`IVRRenderModels_006` definitions from the snapshot pinned at commit
`0924064316de3effbcd1acf1e309182a2deb1c05`. TrackSwap does not redistribute
SteamVR or `openvr_api.dll`.

```text
Copyright (c) 2015, Valve Corporation
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software without
   specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
