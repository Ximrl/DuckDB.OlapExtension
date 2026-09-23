# Third-Party Notices

This project uses the following third-party components. Full texts of the
MIT licenses are available in the respective repositories (see links below).
This file only lists attributions and provides the proprietary license notice
for ADOMD.NET.

---

## 1. DuckDB

- **License:** MIT
- **Homepage:** https://duckdb.org/
- **Source:** https://github.com/duckdb/duckdb
- Copyright © DuckDB Foundation, Amsterdam NL

---

## 2. DuckDB.ExtensionKit

- **License:** MIT
- **Source:** https://github.com/Giorgi/DuckDB.ExtensionKit
- Copyright © Giorgi Dalakishvili

---

## 3. Microsoft.Identity.Client (MSAL.NET)

- **License:** MIT
- **Vendor:** Microsoft Corporation
- **Source:** https://github.com/AzureAD/microsoft-authentication-library-for-dotnet
- Copyright © Microsoft Corporation

Transitively referenced by ADOMD.NET for authentication scenarios.

---

## 4. Microsoft.AnalysisServices.AdomdClient (ADOMD.NET)

- **License:** Proprietary — Microsoft Software License Terms
- **Vendor:** Microsoft Corporation
- **Package:** https://www.nuget.org/packages/Microsoft.AnalysisServices.AdomdClient

### Notice

ADOMD.NET is a proprietary library owned by Microsoft Corporation. It is
**NOT** distributed under an open-source license. Redistribution is permitted
only under the Microsoft Software License Terms for ADOMD.NET, which require:

- Use of the library solely for connecting to Microsoft Analysis Services.
- Retention of all copyright notices and license text.
- No modification of the binary files.
- Distribution as part of the original application (not standalone).

The library is **not included** in the source code of this repository. Users
who wish to build this extension must install the NuGet package
`Microsoft.AnalysisServices.AdomdClient` themselves, thereby accepting
Microsoft's license terms.

Copyright © Microsoft Corporation. All rights reserved.

### Full license text

- https://learn.microsoft.com/en-us/analysis-services/client-libraries
- https://learn.microsoft.com/en-ca/analysis-services/adomd/redistributing-adomd-net
- https://go.microsoft.com/fwlink/?linkid=852895
