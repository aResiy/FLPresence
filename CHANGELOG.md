# Changelog

## 1.3.1
- One-exe install falls back to `<drive>:\Programs\FLPresence` when the system drive is nearly full.

## 1.3.0
- Presence is shown the whole time FL Studio is open (no longer hidden when FL loses focus).
- BPM without MIDI: read from the saved .flp (FL command line / recent files), re-read on save.
  State line: "120 BPM • FL Studio 2024".
- Fix: first presence was sent before Discord was ready, dropped, and then deduped forever.
- Fix: Discord elapsed timer stuck at 0:00 (DiscordRPC treats local time as UTC).

## 1.2.1
- Fix: app now lives exactly as long as FL Studio — exits ~5 s after FL closes (was 10 min)
  and the watchdog is always ensured running (was only started at Windows login, so after
  a fresh install/killed watchdog the app never came back after closing FL).
- Fix: project name flickered "Working on X" ↔ "Producing music" — FL title is now read from
  all FL windows, not Process.MainWindowTitle (which jumps to plugin/dialog windows).
- Fix: Discord elapsed timer was off by the timezone offset (local StartTime vs UTC).
- New: one-exe install — run the downloaded FLPresence.exe, it installs itself
  (%LOCALAPPDATA%\Programs\FLPresence, autostart, Installed apps entry, `--uninstall`).
- New: GitHub Actions release workflow (tag `v*` → FLPresence.exe in Releases).

## 1.2.0 — 2026-09-25

Universal release: FLPresence працює на будь-якій машині **без MIDI-клавіатури**
і **без жодного налаштування** після встановлення.

### Додано
- **Нативне джерело стану (без MIDI, без моста):** моніторинг процесу FL Studio та
  заголовка головного вікна (`FlWindowMonitor`). Дає назву проєкту, версію FL і
  факт фокусу на вікні FL. Присутність показується **лише поки FL у фокусі**:
  - FL у фокусі → «🎛 Working on <проєкт>»;
  - перейшли в інший застосунок → Discord **чистий** (нічого не заявляємо,
    поки ви не продюсуєте).
- **Discord Application ID вбудований як дефолт** — Rich Presence використовує
  локальний IPC pipe і не потребує секретів, тож presence підключається сама
  після встановлення. Свій application — опційно (DISCORD_SETUP.md).
- **Інсталятор сам запускає застосунок** після встановлення; підказки про
  loopMIDI та ручне вставляння App ID прибрані як застарілі.
- Пріоритет джерел: MIDI-міст (багатий стан) > нативний монітор (базовий стан).
  Поява моста миттєво збагачує presence транспортом/патернами/нотами.
- 14 нових тестів (парсер заголовків + нативна логіка движка): 92/92.

### Чому це потрібно
MIDI-міст вимагає, щоб FL Studio запустив controller-скрипт на MIDI-пристрої.
На віртуальних портах (loopMIDI/teVirtualMIDI) FL 24.2.2 build 4597 скрипт
мовчки не імпортує (детально — у LIMITATIONS.md), тому без клавіатури міст
був непрацездатний. Нативне джерело не залежить від MIDI взагалі.

### Виправлено
- **Discord відхиляв activity цілком** через деградований `state` з одного символа
  (шаблон `✏ {Channel} • {Plugin}` з порожніми Channel/Plugin давав `✏`, а Discord
  вимагає ≥ 2). Тепер деградований state взагалі не відправляється (порожній —
  опускається), пуш проходить.
- Нативний режим більше не показує «🎛 Producing music», поки ви в іншому
  застосунку — присутність ховається при втраті фокусу і повертається при
  поверненні у FL.

## 1.1.0 — 2026-09-24

Bugfix / architecture pass: FL-стат більше не залежить від MIDI-клавіатури,
оновлення presence не губляться, діагностика активу ассетів Discord.

### Виправлено
- **Головне:** міст більше не «помирає» після першого пакету. Додано keepalive-потік
  (пінг кожні 2 с, тільки сокет — жодних викликів FL API з чужого потоку), тому
  FL Studio залишається «живим» у presence навіть без жодної MIDI-події.
- **FL-стат не залежить від MPK/клавіатури:** постійним домом моста тепер є
  віртуальний MIDI-порт (loopMIDI або порт «FLPresence» через драйвер teVirtualMIDI,
  який ставиться разом з loopMIDI). Фізична клавіатура — опційне джерело live-нот.
- Dedup-хеш більше не містить хвилинного кошика сесії — Discord не отримує
  фантомних оновлень «нічого не змінилося»; таймер сесії інвалідує dedup тільки
  коли реально скинувся (перезапуск FL).
- Якір сесії тепер оновлюється і при стрибку вперед (перезапуск FL скидав би
  таймер Discord, а він продовжував іти з минулої сесії).
- Авто-призначення скрипта: правильне порівняння Enabled ( значення в реєстрі —
  рядки), віртуальні порти примусово вмикаються, фізичні пристрої з чужим
  скриптом — не чіпаються (як і раніше).
- Кожна зміна стану логується: `FL state changed: BPM 140 -> 150`,
  `Transport: Stopped -> Playing`, `Pattern changed: 1 Pattern 1 -> 4 Verse` тощо.

### Додано
- Міст сам репортує власні помилки (UDP `error`) і пише локальний трейс
  (`%APPDATA%\FLPresence\bridge_trace_*.log`) — діагностика «чому мовчить».
- Діагностика: секції FL Studio / MIDI Sources / Discord / Current Presence,
  перелік MIDI-джерел через winmm, статус віртуального порту, ключі ассетів,
  час останнього SetActivity; кнопка встановлення loopMIDI.
- Диспетчер логує `Discord SetActivity: … (app …)` з точними ключами ассетів і
  `RPC READY: true; Discord Application ID: …` при підключенні.
- Інсталятор пропонує встановити loopMIDI (bundled setup, підтвердження UAC).
- +16 регресійних тестів (dedup, якір сесії, ping/error протокол, FlStateDiffer).
- Автостарт «разом із FL Studio»: `FLPresence.exe --watch` у Run-ключі — фоновий
  лончер стартує додаток лише коли відкривається FL Studio; сам додаток виходить
  через 10 хв після закриття FL (лончер перезапускає його при наступному запускі).
  Старе значення Run-ключа мігрує автоматично при першому старті 1.1.0.

## 1.0.0 — 2026-09-24

Перший реліз.

### Додано
- FL Studio MIDI controller script (`device_FLPresence.py`): збір стану через офіційний
  Scripting API, live MIDI note tracking, UDP heartbeat на localhost.
- Companion (.NET 8, WinForms tray, single-file self-contained):
  - PresenceEngine з пріоритетами Recording > Live MIDI > Playing > Editing > Idle,
    debounce/throttle/dedup (≤1 оновлення Discord/с);
  - chord detector (maj, m, 7, maj7, m7, m7b5, dim, dim7, aug, sus2, sus4, 5, 6, m6,
    add9, madd9, maj9) з fallback на список нот;
  - Secret Mode, Hide-project-only, blacklist;
  - шаблони presence зі змінними {Project}…{FLVersion};
  - tray-меню, діагностика, встановлення/авто-призначення bridge, автозапуск;
  - перепідключення до Discord будь-коли (Discord може стартувати пізніше).
- Інсталятор `installer/install.ps1` / `uninstall.ps1` (per-user, без адміна).
- 62 unit-тести; E2E fake-bridge (`tools/fake_bridge.py`).

### Відомі обмеження
Див. [LIMITATIONS.md](LIMITATIONS.md) (ноти секвенсора не читаються принципово).
