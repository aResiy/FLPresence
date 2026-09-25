# Discord Setup — Application ID за 2 хвилини

> **Це НЕ обов'язково.** З v1.2.0 публічний Application ID FLPresence вбудований
> у застосунок — presence працює одразу після встановлення. Робіть це лише якщо
> хочете показувати presence під **власним** Discord-застосунком (своя назва,
> свої картинки) або якщо вбудований ID колись перестане працювати.

Discord Rich Presence потребує безкоштовний "application" — це просто контейнер для
ID та картинок. Без ботів, токенів і ніяких дозволів до вашого акаунта.

## Крок 1. Створіть application

1. Відкрийте <https://discord.com/developers/applications>
2. Увійдіть у свій Discord-акаунт (у браузері).
3. **New Application** → назва `FLPresence` → **Create**.
4. Вкладка **General Information** → скопіюйте **Application ID**
   (кнопка Copy; виглядає як `1234567890123456789`).

## Крок 2. Вставте ID у FLPresence

Tray → **Open Settings** → поле **Discord Application ID** → **Save**.

Готово — підключення до Discord відбувається автоматично (навіть якщо Discord
запуститься пізніше). Перевірити: tray → Diagnostics → `RPC connected: yes`.

## Крок 3. Завантажте картинки-асети (за бажанням, але рекомендовано)

Без ассетів велика іконка не показуватиметься. У репозиторії готові PNG:

1. У Developer Portal відкрийте ваше application → вкладка **Rich Presence → Art Assets**.
2. **Add Image** для кожного файлу з `assets/discord/`:

| Файл | Name (обов'язково точно так) | Тип |
|---|---|---|
| `flpresence.png` | `flpresence` | Large |
| `play.png` | `play` | Small |
| `stop.png` | `stop` | Small |
| `record.png` | `record` | Small |
| `midi.png` | `midi` | Small |

3. Натисніть **Save Changes**. Зміни підхоплюються одразу, перезапуск не потрібен.

Малюнки — прості геометричні заготовки (темний фон + FL-помаранчевий акцент).
Замініть на власні, якщо хочете — головне зберігайте імена ключів
(або поміняйте ключі у `DiscordPresenceService.Update`).

## Часті питання

**Presence не з'являється?**
- Discord має бути запущений (десктопний клієнт).
- Немає presence, коли Discord запущений у браузері — потрібен сам клієнт.
- Налаштування Discord → Activity Privacy → "Share your detected activities..." — увімкніть.
- Якщо ви вручну додавали FL Studio у Discord "Registered Games" — видаліть звідти,
  інакше Discord показує гру замість Rich Presence.

**Чи це безпечно?**
Локальний IPC-канал Discord (`discord-ipc-0` named pipe) — офіційний механізм Rich Presence.
FLPresence не має токенів, не логініться нікуди і не надсилає нічого в інтернет.
