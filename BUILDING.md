# Building HaulAfterHarvest

## RimWorld 1.7 onwards (.NET 10)

Project: `HaulAfterHarvest.1.7.csproj`, solution: `HaulAfterHarvest.1.7.sln`.

Requirements:
- .NET 10 SDK (`dotnet --list-sdks` should list a `10.0.x` entry)
- RimWorld on 1.7 in Steam: the project references `Assembly-CSharp.dll`, `UnityStubs.dll` and `Waylith.dll`
  from the RimWorld folder two levels up (`..\..\`), which only exist on 1.7
- Harmony from the Workshop (2009463077), referenced from its `1.7\Assemblies` folder

From this folder:

```
dotnet build HaulAfterHarvest.1.7.sln -c 1.7
```

The DLL and pdb are written to `1.7\Assemblies\`. The `1.7` configuration defines `v1_7`.

Visual Studio 2022 cannot open this project for building (the .NET 10 SDK needs MSBuild 18, which ships
with Visual Studio 2026). Use the command above, or Visual Studio 2026 with the `1.7` configuration.

To add a new game version: add `<Configurations>1.7;1.8</Configurations>`, a matching
`<PropertyGroup Condition="'$(Configuration)' == '1.8'">` with `v1_8` in `DefineConstants`, and the
configuration in the solution.

## RimWorld 1.6 (.NET Framework 4.8)

Project: `HaulAfterHarvest.csproj`, configuration `1.6`. It needs the game on 1.6 (it references
`..\..\RimWorldWin64_Data\Managed`) and builds with Visual Studio 2022 or its MSBuild:

```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" HaulAfterHarvest.csproj -p:Configuration=1.6
```
