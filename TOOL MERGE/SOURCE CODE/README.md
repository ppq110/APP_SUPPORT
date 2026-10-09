# Gop CAD APP

App test de gom nhieu file DWG export tu Revit thanh mot file duy nhat `GOP_CAD_MASTER.dwg`.

## Cach hoat dong

- App copy tat ca file `.dwg` vao thu muc tam `CAD_LINKS`.
- App tao script AutoCAD de INSERT cac DWG con vao `GOP_CAD_MASTER.dwg`.
- Tat ca DWG con duoc chen tai `0,0,0`, scale `1`, rotation `0`.
- Sau khi tao xong, chi can gui file `GOP_CAD_MASTER.dwg` sang may khac.

## Chay test

```powershell
cd "D:\DATA\bimlab_dynamo\Gộp CAD APP\GopCadApp"
dotnet run
```

Neu may co AutoCAD/Core Console, app co the chay script va tao master DWG ngay. Neu khong co, app van tao san `.scr` va `.bat` de chay tren may co AutoCAD.

## Gui sang may khac

Gui rieng file:

```text
GOP_CAD_MASTER.dwg
```

May nhan mo Revit va link/import truc tiep file nay.

## Ghi chu ky thuat

Day la che do DWG doc lap, phu hop khi can gui dung mot file cho nguoi khac import/link vao Revit. May tao file van can AutoCAD hoac AutoCAD Core Console de ghi duoc DWG.
