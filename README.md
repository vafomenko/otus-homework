# OTUS Microservice Architecture — Домашнее задание № 2

## Docker-образ приложения

Минимальный HTTP-сервис на C# и ASP.NET Core, подготовленный как walking
skeleton сервиса заказов (`Order Service`) для архитектуры BLT.

Задание демонстрирует:

- реализацию health endpoint;
- контейнеризацию .NET-приложения;
- сборку OCI-образа под `linux/amd64`;
- запуск процесса в контейнере от непривилегированного пользователя;
- автоматическую проверку HTTP-контракта.

## Соответствие требованиям

| Требование | Реализация |
|---|---|
| Сервис отвечает на порту `8000` | Kestrel слушает `0.0.0.0:8000` |
| HTTP-метод `GET /health/` | ASP.NET Core Minimal API |
| Ответ `{"status":"OK"}` | Типизированный JSON response |
| Архитектура образа AMD64 | Сборка с `--platform linux/amd64` |
| Исходный код | [github.com/vafomenko/otus-homework](https://github.com/vafomenko/otus-homework) |
| Dockerfile доступен в GitHub | [Dockerfile](Dockerfile) |
| Образ в Docker Hub | [`vafomenko/blt-orders-api:1.0.0`](https://hub.docker.com/r/vafomenko/blt-orders-api) |

## API

### Проверка состояния

```http
GET /health/ HTTP/1.1
Host: localhost:8000
```

Успешный ответ:

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
```

```json
{"status":"OK"}
```

## Технологии

- C# 14;
- .NET 10;
- ASP.NET Core Minimal API;
- xUnit;
- Docker с multi-stage build.

C# / .NET выбран основным стеком доменных и транзакционных сервисов
архитектуры. Rust предполагается использовать отдельно для компонентов,
где важны высокая пропускная способность и контроль потребления памяти.

## Структура проекта

```text
.
├── src/
│   └── Blt.Orders.Api/
│       ├── Blt.Orders.Api.csproj
│       └── Program.cs
├── tests/
│   └── Blt.Orders.Api.Tests/
│       ├── Blt.Orders.Api.Tests.csproj
│       └── HealthEndpointTests.cs
├── .dockerignore
├── .gitignore
├── Blt.slnx
├── Directory.Build.props
├── Dockerfile
├── global.json
└── README.md
```

## Локальный запуск

Требуется .NET SDK 10.

```powershell
dotnet restore
dotnet test --configuration Release --no-restore
dotnet run --project src/Blt.Orders.Api
```

Проверка из другого терминала:

```powershell
$Response = Invoke-WebRequest http://localhost:8000/health/
$Response.StatusCode
$Response.Content
```

Ожидаемый результат:

```text
200
{"status":"OK"}
```

## Сборка Docker-образа

```powershell
$DockerHubUser = 'vafomenko'
$Image = "$DockerHubUser/blt-orders-api:1.0.0"

docker build --platform linux/amd64 --tag $Image .
```

Проверка целевой платформы:

```powershell
docker image inspect $Image --format '{{.Os}}/{{.Architecture}}'
```

Ожидаемый результат:

```text
linux/amd64
```

## Запуск контейнера

```powershell
docker run --rm `
  --name blt-orders-api `
  --publish 8000:8000 `
  $Image
```

После запуска endpoint доступен по адресу:

```text
http://localhost:8000/health/
```

## Публикация образа

```powershell
docker login --username $DockerHubUser
docker push $Image
```

При авторизации рекомендуется использовать Docker Hub Personal Access Token.
Секреты не должны передаваться в аргументах сборки или сохраняться в
репозитории.

Проверка опубликованного manifest:

```powershell
docker buildx imagetools inspect $Image
```

## Результаты проверки

Перед публикацией проект был проверен локально:

| Проверка | Результат |
|---|---|
| `dotnet test` | 1 тест пройден |
| Платформа Docker-образа | `linux/amd64` |
| Пользователь контейнера | `app` |
| HTTP status | `200 OK` |
| HTTP body | `{"status":"OK"}` |

Multi-stage Dockerfile отделяет SDK от runtime-образа. Контейнер запускается
от встроенного непривилегированного пользователя `app`, а HTTP-заголовок
Kestrel `Server` отключён.
