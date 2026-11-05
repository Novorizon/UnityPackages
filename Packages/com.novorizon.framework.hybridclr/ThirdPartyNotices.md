# Third-party notices

## HybridCLR Unity package

- Source: https://github.com/focus-creative-games/hybridclr_unity
- Tag: v9.0.3
- Commit: 6cadd24d101f4cbd00cd80190106f6621132d24d
- License: MIT; original notice retained verbatim in the installed official HybridCLR package LICENSE.
- Embedded runtime/editor tooling, dnlib dependency, netstandard reference assemblies and package data are retained. Git history and upstream changelog/readme copies were omitted. Upstream implementation files were not modified.

HybridCLR's installer downloads its pinned native HybridCLR and il2cpp_plus sources to the generated `Library/HybridCLR` directory. This directory is not source-controlled. Unity itself and platform SDKs remain separately installed tools, and are not copied into this project.

## dnlib

The upstream package bundles dnlib assembly version 4.3.0.0 in `Plugins/dnlib.dll`.
Source: https://github.com/0xd4d/dnlib/tree/v4.3.0 . Its license notice is retained separately at `ThirdPartyNotices/dnlib.LICENSE.txt`.
