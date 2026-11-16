# CATIA → 2D DWG / DXF / PDF Converter

Windows desktop tool that converts **CATIA V5** 3D files (`.CATPart` / `.CATProduct`) into 2D drawings:

- **DWG** (AutoCAD-compatible)
- **DXF** (interchange)
- **PDF** (optional, same A3 content as the DWG)

The UI is in **Chinese**. Left pane is a real **3D CAD viewport** (HelixToolkit); right pane shows the **2D drawing**.

Repository: https://github.com/ITPyramid85/Catial-Tool-

---

## Features

- Open a single CATPart / CATProduct, or a whole folder of CATIA files
- Batch convert all parts and products with a progress bar
- Dual viewers:
  - **3D** — orbit / pan / zoom / view cube (like AutoCAD / CATIA)
  - **2D** — pan / zoom of the generated A3 sheet
- **Prev / Next** to browse every converted drawing
- Optional **PDF** export matching DWG content
- Auto scale, A3 landscape frame, title block, orthographic + isometric views
- Circle / hole geometry preserved for CATIA V5 viewing
- CLI mode for automation

---

## Requirements

- Windows 10 / 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (to build from source)
- For PDF Chinese text: system fonts such as Microsoft YaHei / SimHei / SimSun

---

## Build

```powershell
cd src\CatiaToDwg
dotnet restore
dotnet build -c Release
```

Publish a self-contained single EXE:

```powershell
dotnet publish src\CatiaToDwg\CatiaToDwg.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -o dist
```

Output: `dist\CatiaToDwg.exe`

---

## Usage (GUI)

1. Run `CatiaToDwg.exe` (no arguments).
2. Set **三维输入** to a CATPart, CATProduct, or folder (sample: `B14`).
3. Set **二维输出文件夹** (default: `output`).
4. Click **打开三维模型** to preview in 3D.
5. Optionally check **生成 PDF 文档**.
6. Click **全部转换为二维图纸**.
7. Use **上一张 / 下一张** to browse results.

### 3D viewer controls

| Action | Control |
|--------|---------|
| Rotate | Left mouse drag |
| Pan | Middle or right mouse drag |
| Zoom | Mouse wheel |
| Standard views | View cube (front / back / left / right / top / bottom) |

---

## Usage (CLI)

```powershell
CatiaToDwg.exe <input> [-o <outputFolder>] [--pdf]
```

Examples:

```powershell
# Convert the B14 sample folder to DWG + DXF
.\dist\CatiaToDwg.exe .\B14 -o .\output

# Also generate PDF
.\dist\CatiaToDwg.exe .\B14 -o .\output --pdf

# Convert one product
.\dist\CatiaToDwg.exe .\B14\B14C-CFR92A.CATProduct -o .\output --pdf
```

---

## Project layout

```
Catial-Tool-/
├── B14/                      # Sample CATIA V5 assembly + parts
├── src/CatiaToDwg/           # Application source
│   ├── MainForm.cs           # Chinese WinForms UI
│   ├── Model3DView.cs        # Helix 3D viewport
│   ├── Drawing2DView.cs      # 2D sheet viewer
│   ├── CatiaParser.cs        # CATIA V5 binary parse
│   ├── Converter.cs          # Batch convert + progress
│   ├── DrawingBuilder.cs     # A3 multi-view sheet
│   ├── CadExport.cs          # DWG / DXF write
│   ├── PdfExport.cs          # PDF write (PDFsharp)
│   └── Program.cs            # GUI + CLI entry
├── .gitignore
└── README.md
```

Generated folders (`dist/`, `output/`, `bin/`, `obj/`) are ignored by Git.

---

## Sample data (`B14`)

| File | Description |
|------|-------------|
| `B14C-CFR92A.CATProduct` | Assembly |
| `B14C-CFR92A.CATPart` | Plate (A1) |
| `B14C-CFR92A-S1.CATPart` | Stiffener 1 |
| `B14C-CFR92A-S1-2.CATPart` | Stiffener 2 |
| `B14C-CFR92A-S1-3.CATPart` | Stiffener 3 |

---

## Tech stack

| Component | Library / tech |
|-----------|----------------|
| UI | WinForms + WPF host |
| 3D viewer | HelixToolkit.Wpf |
| DWG | ACadSharp |
| PDF | PDFsharp |
| Runtime | .NET 10 (Windows) |

---

## Notes

- Input is CATIA V5 (`V5_CFV2`) wireframe recovery (vertices, edges, cylinders/circles).
- Each drawing uses an **A3 landscape** sheet with TOP / ISO / FRONT / RIGHT views and auto scale.
- PDF pages match the same sheet content as DWG/DXF when **生成 PDF** / `--pdf` is enabled.
