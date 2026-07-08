# AxialFlow Designer — ASP.NET Core MVC

Converted from the original 3-tier API + React architecture to a fully integrated
**ASP.NET Core 8 MVC** application with **MySQL 8** via Pomelo EF Core.

---

## Project Structure

```
AxialFanMVC/
├── Controllers/
│   └── Controllers.cs          # HomeController, AccountController, ProjectsController,
│                               #   DesignController, ResultsController
├── Data/
│   └── AxialFanDbContext.cs    # EF Core DbContext — MySQL table mappings + seeding
├── Migrations/
│   ├── 20240101000000_InitialCreate.cs
│   └── AxialFanDbContextModelSnapshot.cs
├── Models/
│   └── Entities.cs             # 8 EF Core entity classes mapping to MySQL tables
├── Services/
│   └── CalcEngines.cs          # AeroCalcEngine + StructCalcEngine (ported from Core layer)
├── ViewModels/
│   └── ViewModels.cs           # All strongly-typed view models
├── Views/
│   ├── Shared/
│   │   └── _Layout.cshtml      # Bootstrap 5 + Chart.js layout
│   ├── Home/
│   │   └── Index.cshtml        # Landing page
│   ├── Account/
│   │   ├── Login.cshtml
│   │   └── Register.cshtml
│   ├── Projects/
│   │   ├── Index.cshtml        # Project cards with pagination
│   │   ├── Create.cshtml
│   │   └── Edit.cshtml
│   ├── Design/
│   │   ├── Wizard.cshtml       # 5-step design wizard
│   │   └── History.cshtml      # Design history table
│   └── Results/
│       └── Result.cshtml       # Full results with Chart.js performance curves
├── wwwroot/
│   └── css/site.css
├── appsettings.json
├── Program.cs                  # DI, Cookie Auth, EF Core, Auto-migrate
├── AxialFanMVC.csproj
└── database_schema.sql         # Raw MySQL DDL (alternative to EF migrations)
```

---

## MySQL Database Tables

| Table               | Purpose                                        |
|---------------------|------------------------------------------------|
| `users`             | User accounts (bcrypt passwords, roles)        |
| `projects`          | Fan design projects per user                   |
| `blade_profiles`    | Lookup table — NACA 4412, 2412, 0012, Flat     |
| `design_inputs`     | 5-step wizard parameters (one row per session) |
| `design_results`    | Aerodynamic + structural calculation outputs   |
| `performance_curves`| Q-ΔP / Q-η / Q-kW curve arrays (mediumtext)   |
| `drawings`          | SVG / DXF / PDF drawing records               |
| `export_logs`       | Audit trail of PDF/DXF/XLSX exports            |

### Entity Relationship Summary

```
users ──< projects ──< design_inputs ──1 design_results ──< performance_curves
                                                         └──< drawings
projects ──< export_logs >── users
blade_profiles ──< design_inputs
```

---

## Setup

### 1. Prerequisites

- .NET 8 SDK
- MySQL 8.0+

### 2. Create MySQL database

```sql
CREATE DATABASE axialfan_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'axialfan_user'@'localhost' IDENTIFIED BY 'your_strong_password';
GRANT ALL PRIVILEGES ON axialfan_db.* TO 'axialfan_user'@'localhost';
FLUSH PRIVILEGES;
```

**Option A** — Let EF Core auto-migrate on startup (default, development):
The `Program.cs` calls `db.Database.Migrate()` at startup automatically.

**Option B** — Run the raw SQL schema:
```bash
mysql -u axialfan_user -p axialfan_db < database_schema.sql
```

**Option C** — EF Core CLI:
```bash
dotnet ef database update
```

### 3. Configure connection string

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=axialfan_db;User=axialfan_user;Password=YOUR_PASSWORD;CharSet=utf8mb4;"
  }
}
```

### 4. Restore NuGet packages

```bash
dotnet restore
```

### 5. Run

```bash
dotnet run
```

The app starts at `https://localhost:5001` (or `http://localhost:5000`).

---

## Architecture: API → MVC Conversion Map

| Original (API + React)                          | Converted (MVC)                                   |
|-------------------------------------------------|---------------------------------------------------|
| `AuthController` (JWT)                          | `AccountController` (Cookie auth, bcrypt)         |
| `ProjectsController` (REST API)                 | `ProjectsController` (MVC CRUD + Razor views)     |
| `DesignController` (REST API)                   | `DesignController` (5-step Wizard + History)      |
| `ResultsController` (REST API)                  | `ResultsController` (Result view + AJAX curves)   |
| `BladeProfilesController` (REST API)            | Embedded in Wizard view dropdown                  |
| React `App.jsx` + components                    | Razor views (Wizard.cshtml, Result.cshtml, etc.)  |
| `PerformanceCurves.jsx` (Chart.js React)        | Inline Chart.js in `Result.cshtml` `@section Scripts` |
| JWT Bearer token auth                           | Cookie authentication (sliding 8h session)        |
| `AeroCalcEngine` / `StructCalcEngine` (Core)   | `Services/CalcEngines.cs` (same logic, same math) |
| Separate `AxialFan.Core` / `Infrastructure` projects | Single MVC project (Models, Data, Services)  |

---

## NuGet Packages

| Package                                  | Version | Purpose                          |
|------------------------------------------|---------|----------------------------------|
| `Pomelo.EntityFrameworkCore.MySql`       | 8.0.2   | MySQL EF Core provider           |
| `Microsoft.EntityFrameworkCore.Design`   | 8.0.8   | EF Core tooling                  |
| `BCrypt.Net-Next`                        | 4.0.3   | Password hashing                 |

CDN (no install needed):
- Bootstrap 5.3.3
- Bootstrap Icons 1.11.3
- Chart.js 4.4.4

### Future packages (add when implementing exports)
```xml
<PackageReference Include="QuestPDF"  Version="2024.3.4" />
<PackageReference Include="ClosedXML" Version="0.102.3" />
<PackageReference Include="netDxf"    Version="3.0.0"   />
```

---

## Features

### Implemented
- ✅ User registration & login (cookie auth, bcrypt)
- ✅ Project CRUD (create, list, edit, delete, pagination)
- ✅ 5-step design wizard with server-side validation
- ✅ Full aerodynamic calculation (specific speed, tip speed, Φ, Ψ, efficiency, shaft power)
- ✅ Structural analysis (centrifugal stress, bending stress, safety factor vs Al 6061-T6)
- ✅ Performance curve generation (Q-ΔP, Q-η, Q-kW) with Chart.js
- ✅ AJAX curve regeneration for any blade angle / RPM
- ✅ Design history per project
- ✅ Warning system for stall, motor overload, tip clearance
- ✅ MySQL auto-migrate on startup
- ✅ NACA 4412/2412/0012 + Flat plate blade profile seed data

### Ready to add
- ⬜ DXF / PDF / Excel export (wire `ResultsController.Download` to `ExportService`)
- ⬜ SVG engineering drawings (wire `DrawingService` to `ResultsController`)
- ⬜ Admin panel (user management, export log view)
- ⬜ PDF report export with QuestPDF

---

## Security Notes

- Passwords hashed with BCrypt (work factor 11)
- Cookie auth is `HttpOnly`, `SlidingExpiration = 8h`
- All controller actions are `[Authorize]` except Home, Account
- Anti-forgery tokens on all POST forms
- Project ownership verified on every query (UserId check)
- **Change the connection string password before deploying**
