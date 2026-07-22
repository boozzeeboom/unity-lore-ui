# unity-lore-ui

**Lore VCS for Unity Editor** — UPM-пакет для работы с [Lore](https://github.com/EpicGames/lore) version control system прямо внутри Unity Editor.

## Установка

```
Window → Package Manager → + → Add package from git URL
```

```
https://github.com/boozzeeboom/unity-lore-ui.git
```

## Быстрый старт

1. После установки: `Lore → Lore Window`
2. Если Lore CLI не найден — откроется мастер установки
3. Мастер скачает `lore.exe` + `loreserver.exe` и запустит сервер
4. Или укажите путь к существующей установке в `Edit → Preferences → Lore`

## Возможности

| Функция | Описание |
|---|---|
| **Server Manager** | Установка, запуск, остановка Lore server |
| **Status** | Просмотр dirty-файлов, stage/unstage |
| **Commit** | Stage all → commit message → commit + push |
| **History** | Список коммитов, детали, unpushed индикатор |
| **Branches** | Список, создание, переключение |
| **Diff** | Raw unified diff для любого файла |

## Структура пакета

```
com.projectc.lore-unity/
├── package.json
├── Editor/
│   ├── LoreWindow.cs           # Главное окно
│   ├── LoreCliService.cs       # Вызов lore.exe
│   ├── LoreCliParser.cs        # Парсинг stdout
│   ├── LoreServerManager.cs    # Управление сервером
│   ├── LoreSettings.cs         # EditorPrefs настройки
│   ├── LoreConfigWindow.cs     # Окно настроек
│   ├── LoreInstallWizard.cs    # Мастер установки
│   ├── LoreWindow.uxml         # UI Toolkit layout
│   └── LoreWindow.uss          # UI Toolkit стили
├── Tests/
│   └── Editor/
│       ├── LoreCliParserTests.cs
│       └── LoreCliServiceTests.cs
└── Documentation~/
    └── index.md
```

## Зависимости

Нет. Чистый Unity Editor API + .NET Standard 2.1.
