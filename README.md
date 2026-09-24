# DuckDB.OlapExtension

[![Release](https://img.shields.io/github/v/release/Ximrl/DuckDB.OlapExtension?sort=semver)](https://github.com/Ximrl/DuckDB.OlapExtension/releases/latest)
[![Build Windows](https://github.com/Ximrl/DuckDB.OlapExtension/actions/workflows/build-windows.yml/badge.svg)](https://github.com/Ximrl/DuckDB.OlapExtension/actions/workflows/build-windows.yml)
[![Build Linux](https://github.com/Ximrl/DuckDB.OlapExtension/actions/workflows/build-linux.yml/badge.svg)](https://github.com/Ximrl/DuckDB.OlapExtension/actions/workflows/build-linux.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Platform: Windows | Linux](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-blue.svg)](#platform-support)

A DuckDB extension that connects to Microsoft Analysis Services (SSAS, Azure Analysis Services, Power BI Premium) and executes DAX queries directly from SQL. Written in C#, compiled to a native binary via .NET Native AOT.

> **⚠️ Platform support:**
> - **Windows** — fully tested, including real SSAS connections.
> - **Linux** — builds successfully and passes CI smoke tests (Native AOT
>   + ADOMD initialization), but **has not been tested against a real SSAS
>   instance**. Linux support relies on ADOMD.NET's .NET Core build and
>   XMLA-over-HTTP (`msmdpump.dll` in IIS); both are theoretically compatible
>   but not verified end-to-end. If you try it and it doesn't work, please
>   open an issue.

## Why this extension

DuckDB already has a community extension `msolap`, but it relies on OLEDB —
a Windows-only COM technology that cannot be ported to Linux and is
incompatible with .NET Native AOT. This extension takes a different approach:

- **Cross-platform by design.** Uses ADOMD.NET (or, on Linux, XMLA over HTTP)
  instead of OLEDB.
- **Single-file native binary.** No .NET Runtime required at runtime.
- **Dynamic schema.** Result columns are determined at query time from the
  server's response — same as any SQL table function.

## Download

Grab the latest release for Windows x64:

**[⬇️ Download latest release](https://github.com/Ximrl/DuckDB.OlapExtension/releases/latest)**

> **Linux users:** prebuilt Linux releases are not yet published. You can
> [build from source](#building) in the meantime. A Linux release will be
> added once it has been verified against a real SSAS instance.

Each release contains a single ZIP archive (Windows):

- `olap.duckdb_extension` — the extension binary
- `DuckDB.OlapExtension.dll` — managed assembly
- `msalruntime.dll` — MSAL native dependency
- `msasxpress.dll` — MSAL compression dependency

Extract the archive into a folder of your choice, then jump to [Installation](#installation).

> Building from source? See [Building](#building).

## Features

- **Dynamic column schema.** Columns are extracted from the XMLA response
  at bind time. A query returning 3 columns produces a 3-column table; a
  query returning 20 columns produces a 20-column table. No fixed schema.
- **Automatic type mapping.** XSD types are mapped to .NET types and then to
  DuckDB types (`xsd:string` → `VARCHAR`, `xsd:int` → `INTEGER`,
  `xsd:double` → `DOUBLE`, `xsd:dateTime` → `TIMESTAMP`, etc.).
- **Works in Native AOT.** The extension avoids ADOMD's internal parser
  (which uses `XmlSerializer` and `Reflection.Emit`, both forbidden in AOT) by
  using `ExecuteXmlReader()` + a custom `XDocument`-based XMLA parser.
- **Authentication:**
  - **Windows** — current Windows user's credentials via SSPI (Windows Authentication). No passwords
    stored anywhere. Fully tested.
  - **Linux** — XMLA-over-HTTP with either Kerberos (`Negotiate`) or Basic
    auth. See [Connection strings](#connection-strings).
- **Diagnostics function.** `olap_test_conn()` verifies that ADOMD works in
  the current environment.

## Requirements

### To use a prebuilt release

- **DuckDB** v1.5.5 or later.
- **Analysis Services instance** — SSAS (on-premises), Azure Analysis Services,
  or Power BI Premium.

Prebuilt releases are currently **Windows only**:

- **Windows 10 build 1904x.5007 or later** (x64).

Linux users: build from source until a verified Linux release is published.

### To build from source

In addition to the above:

- **.NET 10 SDK** — required to build the extension.
- **Python 3** — used by the post-publish script that appends metadata to the
  binary (build-time only).

Platform-specific Native AOT toolchain:

- **Windows:** Visual Studio Build Tools with the C++ workload.
- **Linux:** `clang` and `zlib1g-dev` (both preinstalled on GitHub-hosted
  `ubuntu-24.04` runners; on a fresh system, install with
  `apt-get install clang zlib1g-dev`).

## Building

The project uses two Git submodules: `DuckDB.ExtensionKit` and
`extension-ci-tools`. Clone with `--recurse-submodules`:

```bash
git clone --recurse-submodules https://github.com/Ximrl/DuckDB.OlapExtension.git
cd DuckDB.OlapExtension
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

Then build for your platform:

Windows:
```bash
dotnet publish DuckDB.OlapExtension.csproj -c Release -r win-x64
```

Linux:
```bash
dotnet publish DuckDB.OlapExtension.csproj -c Release -r linux-x64
```

> **Note:** Native AOT does **not** support cross-OS compilation. Building for
> Linux requires a Linux environment (WSL2, Docker, or a native Linux machine).

After a successful build, the output directory contains:

On Windows:
- `olap.duckdb_extension` — the extension itself
- `msalruntime.dll` — MSAL native dependency
- `msasxpress.dll` — MSAL compression dependency

On Linux:
- `olap.duckdb_extension` — the extension itself (`.so` under the hood)
- `DuckDB.OlapExtension.so` — raw native library
- `libmsalruntime.so` — MSAL native dependency

> **Note for Linux:** the dynamic linker does not search the current
> directory by default. When loading the extension, either set
> `LD_LIBRARY_PATH` to the folder containing the extension, or install
> `libmsalruntime.so` system-wide (`/usr/local/lib` + `ldconfig`).

## Installation

Because the extension depends on native DLLs that must be found by the
loader, **do not** use `INSTALL`. Instead:

1. Extract the release archive (or build from source) into a single directory.
   It should contain at least these four files:

       olap.duckdb_extension
       DuckDB.OlapExtension.dll
       msalruntime.dll
       msasxpress.dll

2. Launch DuckDB with the `-unsigned` flag (the extension is not signed):

       duckdb.exe -unsigned

3. Load the extension by full path:

       LOAD 'C:\path\to\olap.duckdb_extension';

The native DLLs are looked up in the same directory as the extension.

> **Tip:** For quick verification, run `SELECT olap_test_conn('Data Source=dummy;');`
> after loading. If it returns `ADOMD OK`, the extension and its native
> dependencies are correctly deployed.

## Usage

### Table function: `query_olap(connection_string, dax_query)`

Executes a DAX query against an Analysis Services instance and returns the
result as a DuckDB table with dynamically determined columns.

```sql
-- Query to OLAP
SELECT * FROM query_olap(
    'Data Source=localhost;Initial Catalog=qOLAP;Integrated Security=SSPI;',
    'EVALUATE VALUES(''DataSources''[Code])'
);
```


```text
┌───────────────────┐
│ DataSources[Code] │
│      varchar      │
├───────────────────┤
│ main              │
│ ext               │
│ dev               │
└───────────────────┘
```

### Scalar function: `olap_test_conn(connection_string)`

Diagnostic function. Creates an `AdomdConnection` object without opening it.
Useful for verifying that ADOMD is alive in a Native AOT build.

```sql
SELECT olap_test_conn('Data Source=dummy;');
-- → ADOMD OK. Type: Microsoft.AnalysisServices.AdomdClient.AdomdConnection. State: Closed
```

## Connection strings

**Windows — TCP, Windows authentication:**
```
Data Source=localhost;Initial Catalog=OLAP;Integrated Security=SSPI;
```

**Linux — HTTP via IIS (`msmdpump.dll`), Basic auth:**
```
Data Source=http://server/olap/msmdpump.dll;Initial Catalog=OLAP;User ID=user;Password=pass;
```

**Linux — HTTP via IIS (`msmdpump.dll`), Kerberos:**
```
Data Source=http://server/olap/msmdpump.dll;Initial Catalog=OLAP;Integrated Security=Negotiate;
```

**Azure Analysis Services:**
```
Data Source=https://<region>.asazure.windows.net/servers/<server>/models/<db>;
```


### The AOT problem and its solution

ADOMD.NET's high-level API (`ExecuteReader()`) relies on `XmlSerializer`,
which generates serialization code at runtime via `Reflection.Emit`. Native
AOT **forbids** `Reflection.Emit` — the native compiler has no JIT to generate
new machine code on the fly. This makes `ExecuteReader()` unusable in an AOT
extension, producing `AdomdUnknownResponseException: The server sent an
unrecognizable response.`

The workaround is to use the low-level `ExecuteXmlReader()`, which returns
the raw XML without parsing it. We parse the XML ourselves with
`System.Xml.Linq` — a fully AOT-compatible API. The format is documented in
the [XMLA specification](https://learn.microsoft.com/en-us/analysis-services/xmla/).

## Limitations

- **DAX only (for now).** The current parser handles the `rowset` response
  format used by DAX queries against tabular models. MDX queries against
  multidimensional cubes (which return `mddataset`) are not yet supported.
- **External native DLLs.** On Windows, the extension requires
  `msalruntime.dll` and `msasxpress.dll`, which come from the ADOMD.NET NuGet
  package. These must be shipped alongside the extension.
- **No pushdown.** Filters in the outer SQL query are not translated to DAX.
  All filtering happens after the full result set is returned from the server.
- **Linux via HTTP only.** TCP connections to Analysis Services are
  Windows-only. On Linux, an XMLA-over-HTTP endpoint (`msmdpump.dll` in IIS)
  is required.

## Development

### Debug console

`tools/DuckDB.OlapExtension.Debug/` contains a small console app that uses
the same `XmlaParser` as the extension. It's the recommended way to debug
parsing logic — you get a full stack trace and can set breakpoints without
rebuilding the AOT binary.

```bash
cd tools/DuckDB.OlapExtension.Debug
dotnet run
```


## License

This project is licensed under the MIT License — see [LICENSE](LICENSE).

Third-party components:

- DuckDB — MIT
- DuckDB.ExtensionKit — MIT
- Microsoft.Identity.Client (MSAL) — MIT
- **Microsoft.AnalysisServices.AdomdClient — proprietary (Microsoft)**

For details and full license terms, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)