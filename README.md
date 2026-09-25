# FLPresence

Discord Rich Presence для **FL Studio**: друзі в Discord бачать, над чим ви працюєте.

```
Грає FLPresence
🎛 Working on recrush
120 BPM • FL Studio 2024
⏱ 15:44
```

## Встановлення (1 хвилина)

1. Завантажте **`FLPresence.exe`** з [Releases](../../releases/latest).
2. Запустіть його один раз → «FLPresence is installed».
3. Все. Відкриваєте FL Studio — статус з'являється в Discord. Закриваєте — зникає.

Без прав адміністратора, без .NET, без налаштувань. Discord має бути запущений (десктопна версія).
Windows може показати SmartScreen («невідомий видавець») → **Детальніше → Все одно запустити**.

**Видалення:** Параметри → Програми → Встановлені програми → FLPresence → Видалити.

## Що дає

- ✅ Назва відкритого проєкту, BPM, версія FL, таймер сесії — автоматично.
- ✅ Стартує **разом з FL Studio** і закривається разом з ним (у фоні висить лише крихітний сторож).
- ✅ Працює **без MIDI-клавіатури**.
- ✅ Все локально: жодних акаунтів, токенів, ботів, інтернет-запитів. FL не модифікується.
- ✅ Приватність: у треї → Settings можна приховати назву проєкту, BPM тощо («Secret Mode»).

## Чого НЕ дає / обмеження

- ❌ **Ноти, акорди, ▶/⏺, патерн, плагін** — тільки якщо є **фізична MIDI-клавіатура**:
  у FL `Options → MIDI Settings → Input → ваш пристрій → Controller type → FLPresence`.
  Без клавіатури FL просто не віддає ці дані.
- ❌ BPM береться зі **збереженого** файлу проєкту — змінили темп, натисніть Ctrl+S.
- ❌ Тільки Windows і десктопний Discord (не браузерний).
- ⚠️ Програма не підписана сертифікатом → попередження SmartScreen при першому запуску.

## Для розробників

Потрібен .NET 8 SDK.

```powershell
dotnet test tests/FLPresence.Tests
powershell -ExecutionPolicy Bypass -File installer\install.ps1   # збірка + встановлення
```

Реліз: `git tag v1.3.0 && git push --tags` → GitHub Actions збирає `FLPresence.exe` у Releases.
Деталі: [ARCHITECTURE.md](ARCHITECTURE.md), [LIMITATIONS.md](LIMITATIONS.md), [CHANGELOG.md](CHANGELOG.md).
