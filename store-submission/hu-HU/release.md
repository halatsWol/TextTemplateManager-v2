### Újdonságok ebben a verzióban

#### Új

- **Az újabb verziók adatai megmaradnak:** a sablonok és mappák mostantól megőrzik, amit egy újabb TextTemplateManager tárol bennük. Ez a verzió mentéskor már nem távolítja el ezeket az adatokat, sem a saját sablonjaidból, sem a megosztott szinkronizálási fájlokból.
- **Kérjük, frissíts erre a verzióra minden számítógépet, amely szinkronizálási fájlt használ közösen.** A következő verziók több adatot tárolnak majd a sablonjaidról, a régebbi verziók pedig továbbra is eltávolítják azokat.

#### Javítva

- Az alkalmazás ismét megnyílik, ha egy szinkronizálási fájl kétszer tartalmazza ugyanazt az elemazonosítót, például ha az ütközési másolatokat kézzel egyesítették. Eddig a második indítástól kezdve ablak nélkül maradt.