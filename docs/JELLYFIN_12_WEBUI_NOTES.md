# Jellyfin 12 WebUI Architektur & File Transformation Info-Datenbank

Diese Dokumentation dient als zentrale Wissensbasis ("Info-Datenbank") für die Funktionsweise der neuen Jellyfin 12 WebUI, die Unterschiede zur klassischen UI (Version 11 und früher) und die Integration des Non-Admin-Menüs über das **File Transformation** Plugin.

---

## 1. Übersicht & Versionssprung (Jellyfin 10/11 → Jellyfin 12)

* **Versionsschema**: Im September 2026 hat das Jellyfin-Projekt das bisherige Versionsschema (10.x / 10.11) umgestellt und direkt **Jellyfin 12.0** veröffentlicht (das Präfix `10.` wurde fallengelassen).
* **Paradigmenwechsel in der WebUI**:
  * **Jellyfin ≤ 11 (Legacy / Classic UI)**: Monolithisches Frontend basierend auf traditionellen JavaScript-Modulen, jQuery-DOM-Manipulation und HTML-Templates (`mainDrawer`, `mainDrawer-scrollContainer`, `customMenuOptions`, `adminMenuOptions`).
  * **Jellyfin 12 (Modern UI)**: Das zuvor als "Experimental" geführte Layout wurde in **"Modern"** umbenannt und ist nun das **Standard-Layout für alle Desktop- und Mobile-Geräte**. Es basiert auf **React 18**, **Material UI (MUI v5)**, **Emotion CSS** und **React Router**. Das alte Layout existiert weiterhin als optionales "Legacy"-Layout in den Anzeigeeinstellungen.

---

## 2. Detaillierte Analyse der Jellyfin 12 Modern UI

### 2.1 Reaktive Layout-Architektur (`AppLayout.tsx`)
In der neuen WebUI steuert `AppLayout.tsx` das Rendering basierend auf Breakpoints (`useMediaQuery((t: Theme) => t.breakpoints.up('md'))`):

```tsx
const isMediumScreen = useMediaQuery((t: Theme) => t.breakpoints.up('md'));
const isDrawerAvailable = isDrawerPath(location.pathname) && Boolean(user) && !isMediumScreen;
```

#### Wichtigste Erkenntnis:
1. **Desktop (`>= md` / ab 900px)**:
   * **Der klassische Sidebar-Drawer existiert auf dem Desktop NICHT MEHR!** (`isDrawerAvailable = false`).
   * Die Bibliotheks- und Navigationslinks befinden sich in der oberen Anwendungsleiste (**AppToolbar**).
2. **Mobile (`< md` / unter 900px)**:
   * Die Hamburger-Schaltfläche öffnet einen Material-UI Drawer (`<ResponsiveDrawer>` mit `<MainDrawerContent>`).
   * Dieser Drawer ist eine MUI-Komponente (`<List>`, `<ListItem>`, `<ListItemButton>`) und **NICHT** die alte `.mainDrawer-scrollContainer`-Struktur.
3. **Legacy Dummy Container (`AppHeader.tsx`)**:
   * Um Abstürze von alten Plugins abzufangen, rendert Jellyfin 12 die alten Klassen versteckt im DOM:
     ```tsx
     <div style={isHidden ? { display: 'none' } : undefined}>
         <div className='mainDrawer hide'>
             <div className='mainDrawer-scrollContainer scrollContainer focuscontainer-y' />
         </div>
     </div>
     ```
   * **Konsequenz**: Wenn ein Skript nach `.mainDrawer-scrollContainer` sucht, findet es entweder gar nichts oder ein unsichtbares Element (`display: none`), wodurch der Menüeintrag für den Nutzer unsichtbar bleibt!

---

### 2.2 Navigationspunkte in Jellyfin 12

In Jellyfin 12 gibt es drei primäre Interaktionsflächen für Navigation:

| Bereich | Komponente | Beschreibung | Zielgruppe |
| :--- | :--- | :--- | :--- |
| **Top Navigation Toolbar** | `UserViewNav.tsx` | Horizontale Buttons in der Headerleiste (Home, Favoriten, Bibliotheken, Custom Links). | Desktop (`>= md`) |
| **User Menu (Avatar Dropdown)** | `AppUserMenu.tsx` | Menü beim Klick auf das Benutzerprofil rechts oben (`#app-user-menu`). Enthält Profil, Einstellungen, Dashboard (nur Admin), Abmelden. | Desktop & Mobile |
| **Mobile Drawer** | `MainDrawerContent.tsx` | Slide-Out Drawer über das Hamburger-Icon. | Mobile (`< md`) |
| **Legacy Drawer** | `.mainDrawer-scrollContainer` | Nur aktiv, wenn Nutzer explizit das "Legacy"-Layout wählt. | Fallback / Altversionen |

---

## 3. Funktionsweise des File Transformation Plugins

Das Plugin **File Transformation** von **IAmParadox27** ([GitHub Repository](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)) ist eine Middleware-Utility für Jellyfin:

### 3.1 Mechanismus
* Es fängt HTTP-Anfragen an statische Webclient-Dateien von `jellyfin-web` ab (z. B. `index.html`, `config.json` etc.) und erlaubt registrierten Plugins, den Dateiinhalt vor dem Ausliefern an den Browser im Arbeitsspeicher zu transformieren ("Runtime Patching").
* In Version 3.0+ für Jellyfin 12 arbeitet File Transformation als ASP.NET Core Middleware.

### 3.2 Registrierung über Reflection (`PluginInterface.RegisterTransformation`)
Plugins registrieren ihre Transformationen ohne feste DLL-Abhängigkeit via Reflection:
```csharp
var payload = new JObject
{
    { "id", PluginGuid },
    { "fileNamePattern", "index.html" },
    { "callbackAssembly", GetType().Assembly.FullName },
    { "callbackClass", typeof(TransformationPatches).FullName },
    { "callbackMethod", nameof(TransformationPatches.IndexHtml) }
};
pluginInterfaceType.GetMethod("RegisterTransformation")?.Invoke(null, new object?[] { payload });
```

### 3.3 Callback-Verhalten
* File Transformation übergibt ein Payload-Objekt (z. B. `PatchRequestPayload` mit `Contents: string`).
* Die Callback-Methode (z. B. `TransformationPatches.IndexHtml`) modifiziert den HTML-String und gibt den neuen Inhalt zurück.
* Im Falle von AniWorld Downloader wird folgendes Script-Tag vor `</body>` injiziert:
  ```html
  <script plugin="AniWorld Downloader" src="../AniWorld/InjectionScript" defer></script>
  ```
* **Direkt-Fallback**: Sollte das File Transformation Plugin nicht installiert sein, greift AniWorld nach einem Timeout auf das direkte Schreiben in die physische `index.html` auf der Festplatte zurück (`_applicationPaths.WebPath/index.html`).

---

## 4. Ursachenanalyse: Warum das Menü in Jellyfin 12 bisher fehlschlug

In der bisherigen `injection.js` existierte folgende Logik:
```javascript
function injectSidebarEntry() {
    if (document.getElementById(MENU_ID)) return;
    var sidebar = document.querySelector('.mainDrawer-scrollContainer');
    if (!sidebar) return;
    var customSection = sidebar.querySelector('.customMenuOptions');
    var adminSection = sidebar.querySelector('.adminMenuOptions');
    ...
    if (customSection) customSection.appendChild(entry);
    else if (adminSection) adminSection.parentNode.insertBefore(entry, adminSection);
    else sidebar.appendChild(entry);
}
```

### Fehlerpunkte in Jellyfin 12:
1. **Kein Drawer auf Desktop**: In Jellyfin 12 Desktop gibt es keine sichtbare Seitenleiste. `.mainDrawer-scrollContainer` ist in einem `display: none` Container versteckt. Das Skript injizierte den Button ins Nichts.
2. **Klassenänderungen auf Mobile**: Der Mobile-Drawer besitzt weder `.customMenuOptions` noch `.adminMenuOptions`. Es handelt sich um MUI-Listen (`MuiList-root`).
3. **MUI Z-Index Konflikt**:
   * Die bisherige Modal-Überlagerung (`aw-modal-overlay`) nutzte `z-index: 999`.
   * In MUI v5 liegt die `AppBar` bei `z-index: 1100`, der `Drawer` bei `1200` und Dialoge/Backdrops bei `1300`. Dadurch wurde das Modal teilweise von nativen UI-Elementen überlagert.
4. **Fehlende Integration ins User-Menü**:
   * Auf Jellyfin 12 ist das Benutzer-Dropdown (`#app-user-menu`) der primäre Ort für administrative und benutzerbezogene Schnellzugriffe. Ein Non-Admin hat keinen Zugriff auf das Dashboard (`/dashboard`), weshalb das User-Menü der ideale, intuitive Ort für den Plugin-Aufruf ist.

---

## 5. Implementierungs-Strategie für Jellyfin 12

Um sowohl Jellyfin 12 (Modern Desktop + Modern Mobile) als auch ältere Versionen / Legacy-Layout nahtlos zu unterstützen, implementieren wir eine **Multi-Target Injektions-Strategie**:

### 5.1 Zielorte für Menüeinträge

1. **User Menu (`#app-user-menu`) [Desktop & Mobile]**:
   * Klick auf das Benutzerprofil-Icon öffnet das MUI Menu.
   * Wir injizieren einen Eintrag im Stil eines `MenuItem`:
     * Klasse: `MuiMenuItem-root MuiMenuItem-gutters ...`
     * Icon: Material Icon `download`
     * Text: `AniWorld Downloader`
   * Funktioniert auf allen Bildschirmgrößen gleichermaßen.

2. **Top Navigation Toolbar (`UserViewNav`) [Desktop]**:
   * Auf Desktop-Bildschirmen wird neben den Bibliotheks-Buttons (Filme, Serien etc.) ein nativer Button eingefügt:
     * Klasse: `MuiButton-root MuiButton-text MuiButton-colorInherit ...`
     * Icon: `<span class="material-icons">download</span>`
     * Label: `AniWorld`
   * Bietet bequemen 1-Klick-Zugriff direkt in der Kopfzeile.

3. **Modern Mobile Drawer (`AppDrawer` / `MainDrawerContent`) [Mobile]**:
   * Sobald der Nutzer auf dem Smartphone das Hamburger-Menü öffnet, wird in die erste MUI-Liste ein ListItem mit Button eingefügt.
   * Klick schließt den Drawer und öffnet das Modal.

4. **Classic / Legacy Fallback (`.mainDrawer-scrollContainer`)**:
   * Überprüfung, ob `.mainDrawer-scrollContainer` existiert UND sichtbar ist (`element.offsetParent !== null`).
   * Falls ja: Injektion der klassischen `navMenuOption lnkMediaFolder`-Klasse wie gehabt.

5. **Direkte Hash-Navigation (`#/aniworld` & `#!/aniworld`)**:
   * Unterstützung für direkte Lesezeichen oder URLs. Beim Aufruf von `#/aniworld` öffnet sich das Modal automatisch.

6. **Modal-Layering**:
   * Anhebung des `z-index` von `999` auf `10000` (über allen MUI AppBars, Drawers und Overlays).

---

## 6. Zusammenfassung der Architektur

```
┌────────────────────────────────────────────────────────┐
│             Jellyfin Server Start                     │
└──────────────────────────┬─────────────────────────────┘
                           │
           EnableNonAdminAccess == true?
                           │
           ┌───────────────┴───────────────┐
           │                               │
          JA                              NEIN
           │                               │
┌──────────▼──────────┐         ┌──────────▼──────────┐
│ File Transformation │         │ CleanupInjection()   │
│ vorhanden?          │         └─────────────────────┘
└────┬───────────┬────┘
     │           │
    JA         NEIN (Timeout 30s)
     │           │
┌────▼──────┐ ┌──▼────────────────┐
│ Runtime   │ │ Physischer Patch │
│ Patch     │ │ in index.html    │
│index.html │ └────────┬─────────┘
└────┬──────┘          │
     │                 │
     └────────┬────────┘
              │
    Lädt im Browser:
    <script src="../AniWorld/InjectionScript" defer>
              │
    ┌─────────▼────────────────────────────────────────┐
    │ injection.js (Intelligente Multi-Target Injektion)│
    │  ├─ Modern Desktop: AppToolbar UserViewNav       │
    │  ├─ Modern Global:  AppUserMenu (#app-user-menu)  │
    │  ├─ Modern Mobile:  AppDrawer MainDrawerContent  │
    │  └─ Legacy/Classic: .mainDrawer-scrollContainer  │
    └──────────────────────────────────────────────────┘
```
