# SshMcp - MCP-сервер для управления компьютером через SSH

[English version](README.en.md)

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/Model_Context_Protocol-server-brightgreen.svg)](https://modelcontextprotocol.io/)
[![SSH.NET](https://img.shields.io/badge/SSH.NET-2026.0.0-blue.svg)](https://github.com/sshnet/SSH.NET)

## Описание проекта

**SshMcp** — это сервер Model Context Protocol (MCP), который позволяет MCP-клиенту, например LM Studio, управлять удалённым компьютером через SSH и SFTP.

Сервис можно запускать как локальное консольное приложение с транспортом `stdio` либо как ASP.NET Core-приложение с HTTP-транспортом. Подключение к удалённой машине настраивается на стороне сервера; модель получает только MCP-инструменты и не видит SSH-секреты.

## Ключевые функции

- **Выполнение команд через SSH** — запуск shell-команд на удалённом компьютере
- **Рабочий каталог** — опциональный `cd` перед выполнением команды
- **Загрузка файлов через SFTP** — чтение файла с локального диска по пути и отправка на сервер
- **Скачивание файлов через SFTP** — сохранение файла с сервера на локальный диск по пути
- **Аутентификация по паролю** — поддержка SSH password authentication
- **Аутентификация по ключу** — поддержка приватных ключей и passphrase
- **Два транспорта MCP** — `stdio` для LM Studio и `HTTP` для удалённых клиентов
- **Несколько SSH-серверов** — преднастроенные профили и выбор сервера параметром `server`
- **Ограничение команд** — настройка разрешённых префиксов команд
- **Ограничение вывода** — защита от чрезмерно больших ответов модели
- **Таймауты** — отдельные таймауты SSH-соединения и выполнения команды
- **Управление передачей файлов** — возможность полностью отключить SFTP
- **Конфигурация через JSON и ENV** — секреты можно не хранить в файле конфигурации
- **Self-contained публикация** — приложение можно публиковать без установленного .NET Runtime

## Архитектура

```text
SshMcp/
├── Configuration/
│   └── SshOptions.cs       # Параметры SSH-подключения и ограничения
├── Services/
│   └── SshService.cs       # SSH/SFTP-подключение и выполнение операций
├── Tools/
│   └── SshTools.cs         # MCP-инструменты
├── sshmcp.settings.json.example # Пример конфигурации
├── Program.cs              # Выбор транспорта, DI и запуск приложения
├── SshMcp.csproj
└── README.md
```

## Инструменты MCP Server

### SSH Tools

| Название инструмента | Описание | Параметры |
|---|---|---|
| `execute_command` | Выполнение shell-команды на удалённом компьютере. Если `withSudo=true`, команда автоматически запускается с повышенными правами — добавлять `sudo` в `command` не нужно | `command`, `workingDirectory` (опционально), `withSudo` (опционально) |
| `list_ssh_servers` | Список доступных профилей без секретов и параметров подключения | нет |
| `upload_file` | Загрузка файла с локального диска на удалённый компьютер через SFTP | `localPath`, `remotePath`, `withSudo` (опционально), `server` (опционально) |
| `download_file` | Скачивание файла с удалённого компьютера на локальный диск через SFTP | `remotePath`, `localPath`, `withSudo` (опционально), `server` (опционально) |

`execute_command` возвращает объект:

```json
{
  "stdout": "...",
  "stderr": "...",
  "exitCode": 0
}
```

Содержимое файлов не передаётся через MCP: модель указывает только локальный и удалённый пути, а сервис читает и сохраняет файлы сам. Для больших файлов используется потоковая передача SFTP.

### Передача файлов

Загрузка файла на сервер:

```json
{
  "name": "upload_file",
  "arguments": {
    "localPath": "C:\\temp\\nginx.conf",
    "remotePath": "/etc/nginx/nginx.conf",
    "withSudo": true,
    "server": "production"
  }
}
```

Скачивание файла с сервера:

```json
{
  "name": "download_file",
  "arguments": {
    "remotePath": "/var/log/nginx/error.log",
    "localPath": "C:\\temp\\error.log",
    "withSudo": true,
    "server": "production"
  }
}
```

При `withSudo: true` используется промежуточный файл в `/tmp`:

- загрузка: SFTP пишет файл в `/tmp/ssh-mcp-<id>`, затем `sudo mv` переносит его в `remotePath`;
- скачивание: `sudo cp` создаёт копию в `/tmp/ssh-mcp-<id>`, меняет владельца на SSH-пользователя, затем SFTP скачивает файл и временный файл удаляется через `sudo rm`.

Добавлять `sudo` в пути или команды не нужно — привилегии повышает сам инструмент.

## Безопасность

1. **SSH-секреты хранятся на сервере** — пароль и приватный ключ не являются параметрами MCP-инструментов.
2. **Ограничение команд** — при заполненном `AllowedCommandPrefixes` команда должна начинаться с одного из заданных префиксов.
3. **Ограничение времени выполнения** — долгие команды завершаются после `CommandTimeoutSeconds`.
4. **Ограничение размера вывода** — ответ обрезается до `MaxOutputCharacters`.
5. **Контроль передачи файлов** — `AllowFileTransfer: false` отключает `upload_file` и `download_file`.
6. **Shell-квотинг рабочего каталога** — путь рабочего каталога экранируется перед вставкой в команду.
7. **Локальный HTTP по умолчанию** — для доступа из LM Studio рекомендуется слушать `127.0.0.1`, а не публиковать сервис во внешнюю сеть.
8. **TLS и аутентификация для внешнего HTTP** — при публикации HTTP через сеть используйте reverse proxy с HTTPS и дополнительной аутентификацией.
9. **Sudo-пароль не передаётся модели** — при `withSudo: true` пароль берётся только из `Ssh:SudoPassword` и передаётся SSH-команде через stdin; он не добавляется в текст shell-команды.
10. **Профили не раскрывают секреты** — `list_ssh_servers` возвращает только имя и описание профиля.
11. **Доступ к локальной файловой системе** — `upload_file` может прочитать любой доступный файл на машине с MCP, а `download_file` — записать файл в любой доступный локальный путь. Запускайте сервис от отдельной учётной записи с минимальными правами и не выставляйте его недоверенным клиентам.

> `execute_command` фактически предоставляет пользователю модели права SSH-учётной записи. Используйте отдельную учётную запись с минимально необходимыми правами и задавайте `AllowedCommandPrefixes`, если полный shell-доступ не требуется.

## Использование

### Подготовка конфигурации

Скопируйте пример конфигурации:

```powershell
Copy-Item sshmcp.settings.json.example sshmcp.settings.json
```

Заполните секцию `Ssh` либо задайте параметры через переменные окружения.

### Запуск через stdio для LM Studio

```powershell
$env:SSH_MCP_TRANSPORT = "stdio"
$env:Ssh__Host = "192.168.1.10"
$env:Ssh__Port = "22"
$env:Ssh__Username = "remote-user"
$env:Ssh__PrivateKeyPath = "C:\Users\me\.ssh\id_ed25519"

dotnet run -c Release -- --stdio
```

Для запуска опубликованного приложения:

```powershell
$env:SSH_MCP_TRANSPORT = "stdio"
& ".\bin\Release\net10.0\win-x64\publish\SshMcp.exe" --stdio
```

В настройках MCP-клиента укажите исполняемый файл и аргумент `--stdio`. Пример конфигурации:

```json
{
  "mcpServers": {
    "ssh-remote": {
      "command": ".\\bin\\Release\\net10.0\\win-x64\\publish\\SshMcp.exe",
      "args": ["--stdio"],
      "env": {
        "SSH_MCP_TRANSPORT": "stdio",
        "Ssh__Host": "192.168.1.10",
        "Ssh__Port": "22",
        "Ssh__Username": "remote-user",
        "Ssh__PrivateKeyPath": "C:\\Users\\me\\.ssh\\id_ed25519"
      }
    }
  }
}
```

MCP JSON-RPC передаётся через `stdin/stdout`. Диагностические сообщения приложения направляются в `stderr`.

### Запуск через HTTP

```powershell
$env:SSH_MCP_TRANSPORT = "http"
$env:Ssh__Host = "192.168.1.10"
$env:Ssh__Username = "remote-user"
$env:Ssh__PrivateKeyPath = "C:\Users\me\.ssh\id_ed25519"

dotnet run -c Release -- --urls http://127.0.0.1:6258
```

MCP endpoint:

```text
http://127.0.0.1:6258/mcp
```

Пример конфигурации HTTP-клиента:

```json
{
  "mcpServers": {
    "ssh-remote": {
      "url": "http://127.0.0.1:6258/mcp"
    }
  }
}
```

## Аргументы командной строки и режимы

| Аргумент/переменная | Описание |
|---|---|
| `--stdio` | Запускает MCP stdio-транспорт, если `SSH_MCP_TRANSPORT` не задана |
| `SSH_MCP_TRANSPORT=stdio` | Явно включает stdio-транспорт |
| `SSH_MCP_TRANSPORT=http` | Включает HTTP-транспорт |
| `--urls http://127.0.0.1:6258` | Адрес и порт ASP.NET Core для HTTP-режима |

Если `SSH_MCP_TRANSPORT` не задана и аргумент `--stdio` отсутствует, используется HTTP-режим.

## Конфигурация

Сервис читает секцию `Ssh` из `sshmcp.settings.json`, переменных окружения и стандартных источников конфигурации .NET. Файл `sshmcp.settings.json` ищется в каталоге, где находится `SshMcp.exe`, и имеет приоритет над `appsettings.json`; переменные окружения переопределяют файл.

```json
{
  "Ssh": {
    "Host": "192.168.1.10",
    "Port": 22,
    "Username": "remote-user",
    "Password": "",
    "PrivateKeyPath": "C:\\Users\\me\\.ssh\\id_ed25519",
    "PrivateKeyPassphrase": "",
    "SudoPassword": "",
    "ConnectionTimeoutSeconds": 15,
    "CommandTimeoutSeconds": 60,
    "MaxOutputCharacters": 100000,
    "AllowFileTransfer": true,
    "AllowedCommandPrefixes": [
      "uname",
      "whoami",
      "systemctl status"
    ],
    "Servers": {
      "production": {
        "Description": "Production Linux server",
        "Host": "192.168.1.20",
        "Port": 22,
        "Username": "deploy",
        "PrivateKeyPath": "C:\\Users\\me\\.ssh\\id_ed25519",
        "SudoPassword": ""
      }
    }
  }
}
```

Параметры:

- `Host` — имя или IP удалённого SSH-сервера
- `Port` — SSH-порт, по умолчанию `22`
- `Username` — имя SSH-пользователя
- `Password` — пароль SSH; рекомендуется задавать через ENV
- `PrivateKeyPath` — путь к приватному ключу
- `PrivateKeyPassphrase` — passphrase приватного ключа
- `SudoPassword` — пароль sudo; используется только при `withSudo: true`, рекомендуется задавать через ENV или Secret Manager
- `ConnectionTimeoutSeconds` — таймаут установления соединения, по умолчанию `15`
- `CommandTimeoutSeconds` — таймаут выполнения команды, по умолчанию `60`
- `MaxOutputCharacters` — максимальный размер `stdout` и `stderr`, по умолчанию `100000`
- `AllowFileTransfer` — разрешает или запрещает SFTP-инструменты
- `AllowedCommandPrefixes` — список разрешённых префиксов команд; пустой массив разрешает команды без фильтра по префиксу
- `Servers` — словарь преднастроенных профилей. Ключ (`production`) передаётся в параметре `server` MCP-инструментов
- `Servers.<name>.Description` — безопасное описание, которое возвращает `list_ssh_servers`
- Параметры профиля (`Host`, `Port`, `Username`, `Password`, `PrivateKeyPath`, `SudoPassword` и ограничения) переопределяют соответствующие значения корневой секции `Ssh`

### Выбор SSH-сервера

Сначала модель вызывает:

```json
{
  "name": "list_ssh_servers",
  "arguments": {}
}
```

Затем передаёт имя профиля в каждый вызов:

```json
{
  "name": "execute_command",
  "arguments": {
    "server": "production",
    "command": "systemctl status nginx",
    "withSudo": true
  }
}
```

### Конфигурация через переменные окружения

Двойное подчёркивание `__` используется как разделитель секций .NET:

| Параметр JSON | Переменная окружения |
|---|---|
| `Ssh:Host` | `Ssh__Host` |
| `Ssh:Port` | `Ssh__Port` |
| `Ssh:Username` | `Ssh__Username` |
| `Ssh:Password` | `Ssh__Password` |
| `Ssh:PrivateKeyPath` | `Ssh__PrivateKeyPath` |
| `Ssh:PrivateKeyPassphrase` | `Ssh__PrivateKeyPassphrase` |
| `Ssh:SudoPassword` | `Ssh__SudoPassword` |
| `Ssh:AllowFileTransfer` | `Ssh__AllowFileTransfer` |

ENV имеет приоритет над значениями из `sshmcp.settings.json`. Не добавляйте реальные пароли и passphrase в Git.

## Сборка и публикация

### Обычная сборка

```powershell
dotnet restore
dotnet build -c Release
```

### Self-contained публикация для Windows x64

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Результат будет находиться в:

```text
bin\Release\net10.0\win-x64\publish\
```

### Другие платформы

Проект содержит следующие Runtime Identifier:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `linux-musl-x64`
- `osx-arm64`

Пример публикации для Linux:

```bash
dotnet publish -c Release -r linux-x64 --self-contained true
```

## Тестирование

Проверка сборки:

```powershell
dotnet build -c Release
```

Проверка запуска stdio без подключения к SSH-серверу:

```powershell
$env:SSH_MCP_TRANSPORT = "stdio"
dotnet run -c Release -- --stdio
```

Процесс должен запуститься и ожидать MCP-сообщения через стандартный ввод. Для проверки SSH укажите рабочие параметры подключения и вызовите инструмент `execute_command` через MCP-клиент.

## Технологии

- [.NET 10](https://dotnet.microsoft.com/)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)
- [SSH.NET](https://github.com/sshnet/SSH.NET)
- [ASP.NET Core](https://learn.microsoft.com/aspnet/core/)

## Ссылки

- [Model Context Protocol](https://modelcontextprotocol.io/) — официальный сайт MCP
- [C# MCP SDK](https://csharp.sdk.modelcontextprotocol.io/) — документация MCP SDK для .NET
- [LM Studio](https://lmstudio.ai/) — локальный клиент и среда для моделей
- [SSH.NET](https://github.com/sshnet/SSH.NET) — библиотека SSH/SFTP для .NET

## Лицензия

Проект распространяется по лицензии [MIT](LICENSE.txt).

Полный текст лицензии находится в файле [`LICENSE.txt`](LICENSE.txt). Лицензия разрешает использовать, копировать, изменять, публиковать и распространять проект при сохранении уведомления об авторских правах и текста лицензии.

---

**Версия:** 0.1.0-beta  
**Последнее обновление:** 2026
