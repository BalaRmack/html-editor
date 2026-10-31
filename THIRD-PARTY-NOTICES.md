# Third-party notices

LocalVoice itself is MIT-licensed. The following components are distributed with
the application. Models are downloaded separately, only on user request.

| Component | Pin | License | Source |
|---|---|---|---|
| .NET / WPF / Windows Desktop runtime | SDK 8.0.4xx, runtime rolls forward at publish | MIT | https://github.com/dotnet/runtime |
| Whisper.net | 1.8.1 | MIT | https://github.com/sandrohanea/whisper.net/tree/1.8.1 |
| whisper.cpp / Whisper.net.Runtime CPU | 1.8.1 | MIT | https://github.com/ggml-org/whisper.cpp |
| NAudio | 2.2.1 | MIT | https://github.com/NAudio/NAudio/tree/v2.2.1 |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | https://github.com/CommunityToolkit/dotnet/tree/v8.4.0 |
| OpenAI Whisper GGML model weights | Hugging Face revision in ModelCatalog.cs | MIT | https://huggingface.co/ggerganov/whisper.cpp |
| xUnit / Microsoft.NET.Test.Sdk | See test csproj | Apache-2.0 / MIT | Build/test only, not installed with the application |
| Inno Setup | 6.7.4 in CI | Inno Setup license (free compiler) | https://jrsoftware.org/isinfo.php |

The self-contained publish includes `LICENSE.md`, `THIRD-PARTY-NOTICES.md`, and
the .NET runtime's `LICENSE.txt` / `ThirdPartyNotices.txt`. NuGet package license
texts must also be collected into the release's `licenses/` directory by the
release packaging step; do not remove upstream copyright notices.

Whisper model weights are copyright OpenAI and contributors, MIT. The GGML
conversion and quantization do not remove the upstream license. Model URLs and
SHA-256 hashes are pinned in `ModelCatalog.cs`. A checksum verifies integrity,
not the legal status or fitness of a model. Review the upstream model card before
redistributing weights. No model weights or microphone recordings are in Git.

Inno Setup is a build-time tool, not a paid application dependency. Its installer
stub has its own permissive license; see the version installed on the build host.
No proprietary speech SDK, cloud subscription, Python runtime or browser runtime
is required.
