### Neuigkeiten in dieser Version

#### Neu

- **Vorlagen in mehreren Sprachen:** Eine Vorlage kann ihren Text jetzt in mehreren Sprachen enthalten, eine pro Tab über der Symbolleiste des Editors, mit einem Titel und einem Satz Kürzel. **+** fügt eine Sprache hinzu (aus dem Text erkannt, wo möglich), und **Übersetzen ▸ Als Sprache hinzufügen** übersetzt in einen neuen Tab. Der erste Tab ist der Standard, der eingefügt wird; **Sprache kopieren** im Rechtsklick-Menü der Liste, **Sprache einfügen** in Quick Paste und **← / →** in der Tastenfolge-Liste von Quick Paste wählen eine andere. Die Suche durchsucht alle Sprachen.
- **Browsererweiterung:** Mit TTM-Connect 1.2 oder neuer fügst du auch die anderen Sprachen einer Vorlage ein und speicherst eine Auswahl als Sprache einer Vorlage. Ältere Versionen der Erweiterung funktionieren weiter wie bisher.
- **Synchronisierte Ordner:** Vorlagen in mehreren Sprachen brauchen auf jedem PC, der eine gemeinsame Datei speichert, TextTemplateManager 2.7 oder neuer; TextTemplateManager 1 und 2.6 oder älter entfernen die anderen Sprachen beim Speichern.

#### Verbessert

- **Läuft mit .NET 10:** TextTemplateManager läuft jetzt mit .NET 10, für das Microsoft bis November 2028 Sicherheitsupdates liefert. .NET 8, das ältere Versionen nutzen, bekommt seit dem 10. November 2026 keine mehr. Deine Vorlagen, Einstellungen und Arbeitsweise bleiben gleich.
- **Deutlichere Symbole:** Ordner und Vorlagen sind in der Liste und auf den Schaltflächen zum Hinzufügen besser zu unterscheiden.

#### Behoben

- **Gemeinsame Dateien:** Beim Speichern in eine synchronisierte Datei werden Änderungen, die ein anderer PC seit dem Lesen gemacht hat, nicht mehr überschrieben; sie werden bei der nächsten Prüfung zusammengeführt.
- **Beenden:** Beim Beenden wird jetzt auf ein bereits laufendes Speichern der Einstellungen gewartet.