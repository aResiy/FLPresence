# Архітектура FLPresence

## Загальна схема

```
┌─────────────────────────────┐          ┌──────────────────────────────────┐
│  FL Studio                  │          │  FLPresence Companion (.NET 8)   │
│  ┌───────────────────────┐  │          │  ┌────────────┐  ┌────────────┐  │
│  │ FLPresence Bridge     │  │  UDP JSON│  │ IPC        │→ │ Presence   │  │
│  │ (MIDI controller      │──┼──────────┼→ │ (UDP :39901│  │ Engine     │  │
│  │  script, official API)│  │ localhost│  │  loopback) │  │ (пріоритет,│  │
│  └───────────────────────┘  │  only    │  └────────────┘  │ dedup)     │  │
└─────────────────────────────┘          │                  └─────┬──────┘  │
                                         │  ┌────────────┐        │         │
                                         │  │ Tray + UI  │  ┌─────▼──────┐  │
                                         │  │ (WinForms) │  │ Discord RPC│──┼── discord-ipc-N pipe
                                         │  └────────────┘  └────────────┘  │
                                         └──────────────────────────────────┘
```

Дві частини, єдиний канал між ними — UDP `127.0.0.1:39901`:

- **Bridge** (`flstudio/FLPresence/device_FLPresence.py`) — офіційний FL Studio MIDI
  controller script. Живе всередині FL, збирає стан через Scripting API, шле маленькі
  JSON-дейтаграми. Fire-and-forget: якщо Companion не запущений, пакети просто губляться —
  скрипт не може ні заблокувати, ні зламати FL.
- **Companion** — системний трей (.NET 8 WinForms, single-file). Слухає UDP, будує
  presence, спілкується з Discord через локальний named pipe (`discord-ipc-N`),
  керує налаштуваннями і встановленням бриджа.

## Чому саме так (і чим це краще за референси)

| Проєкт | Метод | Проблема |
|---|---|---|
| `nuiiv/FL-Studio-Discord-RPC` | AOB memory scanning `FLEngine_x64.dll` | ламається з білдами FL; чтение чужої пам'яті |
| `Gluton-Official/FLRPC` | Cheat Engine table / memory addresses | жорстка прив'язка версій (FL 21.1 ↔ FLRPC 0.2.1) |
| `Myuuiii/DAWPresence` | парсинг заголовка вікна | крихко, тільки те, що в title |
| **FLPresence** | **офіційний MIDI Scripting API** | стабільно між версіями; жодних вторгнень у процес FL |

Плюс саме UDP для IPC, а не TCP/named pipe: читача може не бути — дейтаграма губиться,
записувач не блокується (критично для аудіо-застосунку).

## Дані (state snapshot)

Bridge шле три типи пакетів:

- `state` — повний снапшот: зміни (debounce 250 мс) + heartbeat кожні 2 с;
- `notes` — набір затиснутих MIDI-нот при кожному Note On/Off (throttle 150 мс);
- `hello` / `bye` — lifecycle.

Поле `session` (секунди від ініціалізації скрипта) дає Companion'у стабільний
Discord-таймстамп "elapsed" без пересилок.

Якщо heartbeat зник на >15 с — Companion вважає FL закритим і очищає presence.

## PresenceEngine

- **Пріоритет режимів**: Recording > Live MIDI > Playing > Editing > Idle.
- **Live MIDI hold**: після останньої ноти статус тримається `MidiHoldSeconds` (4 с за
  замовчуванням), потім повертається до попереднього режиму.
- **Dedup**: MD5-хеш від (details, state, small icon, хвилина session-start) — невидимі
  зміни (позиція відтворення, heartbeat) не шлються взагалі.
- **Throttle**: не частіше `UpdateIntervalMs` (1 с за замовчуванням) — Discord отримує
  максимум 1 оновлення/с, а зазвичай кілька на хвилину.
- **Privacy** (`PrivacyFilter`): Secret Mode / Hide-project-only / blacklist — застосовується
  ДО рендерингу, тому секретні значення фізично не потрапляють у payload.

## Компоненти рішення

```
src/
  FLPresence.Core/       FlState, PresenceEngine, ChordDetector, TemplateRenderer,
                         PrivacyFilter, AppSettings, FileLogger (без залежностей)
  FLPresence.IPC/        UdpBridgeListener + BridgeProtocol (парсинг)
  FLPresence.Discord/    DiscordPresenceService (обгортка DiscordRichPresence/Lachee)
  FLPresence.Companion/  Tray-додаток: Program (TrayAppContext), SettingsForm,
                         DiagnosticsForm, BridgeInstaller (deploy + авто-призначення)
tests/FLPresence.Tests/  62 unit-тести (xUnit)
flstudio/FLPresence/     device_FLPresence.py (сам бридж)
installer/               install.ps1 / uninstall.ps1
tools/                   fake_bridge.py (E2E без FL), gen_assets.py
assets/                  flpresence.ico, discord/*.png (Rich Presence assets)
```

Єдина зовнішня залежність — NuGet `DiscordRichPresence` (підтримуваний C#-клієнт
офіційного Discord IPC; MIT).

## Авто-призначення скрипта в FL

FL Studio зберігає controller type MIDI-входу у registry:
`HKCU\Software\Image-Line\FL Studio <ver>\Devices\MIDI input\<device>\ScriptFolder`.
Інсталятор/Companion заповнюють це поле значенням `FLPresence` **тільки якщо воно
порожнє** (існуючі скрипти типу Mackie не перетираються) і тільки коли FL не запущений.
Якщо поле вже зайняте — показується коротка інструкція ручного вибору.

## Запуск/перевірка без FL Studio

`python tools/fake_bridge.py <edit|play|record|chord|bye|timeout>` — шле реальні пакети
у Companion, повний ланцюжок (IPC → Engine → Discord payload) працює так само.
