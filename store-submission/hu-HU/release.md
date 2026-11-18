### Újdonságok ebben a verzióban

#### Új

- **Sablonok több nyelven:** egy sablon mostantól több nyelven is tartalmazhatja a szövegét, nyelvenként egy lapon a szerkesztő eszköztára felett, egy címmel és egy rövidítéskészlettel. A **+** új nyelvet ad hozzá (ha lehet, a szövegből felismerve), a **Fordítás ▸ Hozzáadás nyelvként** pedig új lapra fordít. Az első lap az alapértelmezett, ez illesztődik be; a lista jobb gombos menüjében a **Nyelv másolása**, a Quick Paste-ben a **Nyelv beillesztése**, a Quick Paste billentyűsor-listáján pedig a **← / →** másikat választ. A keresés minden nyelvben keres.
- **Böngészőbővítmény:** a TTM-Connect 1.2-es vagy újabb verziójával a sablon többi nyelvét is beillesztheted, és egy kijelölést a sablon egyik nyelveként mentheted. A bővítmény régebbi verziói ugyanúgy működnek, mint eddig.
- **Szinkronizált mappák:** a több nyelvű sablonokhoz minden gépen, amely megosztott fájlt ment, TextTemplateManager 2.7 vagy újabb kell; a TextTemplateManager 1 és a 2.6-os vagy régebbi verziók mentéskor eltávolítják a többi nyelvet.

#### Továbbfejlesztve

- **.NET 10 alapon fut:** a TextTemplateManager mostantól .NET 10-en fut, amelyhez a Microsoft 2028 novemberéig ad biztonsági frissítéseket. A korábbi verziók által használt .NET 8 2026. november 10. óta nem kap többet. A sablonjaid, a beállításaid és a használat módja nem változik.
- **Jobban megkülönböztethető ikonok:** a mappák és a sablonok könnyebben megkülönböztethetők a listában és a hozzáadás gombjain.

#### Javítva

- **Megosztott fájlok:** a szinkronizált fájlba mentés már nem írja felül azokat a módosításokat, amelyeket egy másik gép a beolvasás óta végzett; ezek a következő ellenőrzéskor egyesülnek.
- **Kilépés:** kilépéskor az alkalmazás megvárja a beállítások már folyamatban lévő mentését.