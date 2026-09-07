# Домашнее задание № 5 - мониторинг и Grafana

Ветка `homework-5` продолжает ДЗ № 4: CRUD пользователей на .NET 10,
PostgreSQL, Kubernetes и Helm. Добавлены Prometheus, Grafana, HTTP-метрики,
алерты, нагрузочный тест и задание со звёздочкой.

## Материалы для сдачи

- [JSON-дашборд](monitoring/grafana/dashboards/blt-users-overview.json).
- [Пять скриншотов настоящей Grafana](docs/screenshots/README.md).
- [JSON Grafana Alerting: 4 правила и contact point](monitoring/grafana/alerting/alerts.json).
- [Результаты нагрузки и проверки метрик](docs/results/README.md).
- [Описание метрик, PromQL, алертов и сценария](docs/monitoring.md).

| Требование | Реализация |
|---|---|
| Latency p50/p95/p99/max по API-методам | Гистограммы по method/route и точный sliding max за минуту |
| RPS и количество HTTP 500 по API-методам | Отдельные временные ряды всех пяти CRUD-методов |
| Latency, RPS, HTTP 500 по сервису от ingress | Стандартные метрики nginx-ingress-controller; max - явно обозначенная оценка гистограммы |
| Grafana Alerting | Error Rate > 1% и p95 > 300 ms, по API и ingress; pending 1 минута |
| CPU и память pod’ов ★ | Существующие kubelet/cAdvisor metrics, отдельный график каждой реплики |
| Экспортёр БД и её метрики ★ | postgres_exporter, роль pg_monitor, соединения, TPS, строки, cache hit, deadlocks |
| Скриншоты под нагрузкой | Десятиминутный CRUD-тест через ingress с фазой управляемых сбоев и восстановлением |

**Проверено 07.09.2026:** 222 410 запросов за 10 минут, ноль неожиданных
ответов; четыре алерта сработали и восстановились. Намеренно введено
1 494 ответа HTTP 500. Все 30 запросов дашборда возвращают реальные метрики,
5 тестов .NET проходят.

## Запуск в Windows / Docker Desktop

Нужны работающий Docker Desktop с Linux containers, PowerShell и kubectl.
Скрипт скачает kind и Helm в игнорируемую `.tools`, создаст отдельный кластер
`otus-hw5`, соберёт образ локально и загрузит его в kind. Push образа не нужен.
Первый запуск скачивает Kubernetes и образы мониторинга и может занять время.

```powershell
.\scripts\setup.ps1
```

Kubeconfig: `.local/kubeconfig`; команды не переключают ваш основной context.
Внешние порты привязаны к loopback, файл hosts менять не требуется.

| Адрес | Назначение |
|---|---|
| http://localhost:3005/d/blt-users-overview | Grafana, dashboard открыт для просмотра |
| http://localhost:3005/login | Вход для просмотра Alerting: `admin` / `homework5` |
| http://localhost:9095 | Prometheus, в том числе `/targets` |
| http://localhost:8085 | API через ingress, заголовок `Host: arch.homework` |

```powershell
curl.exe -H "Host: arch.homework" http://localhost:8085/health/
.\scripts\run-load.ps1 -Smoke
.\scripts\run-load.ps1
python scripts/verify-monitoring.py --require-faults
```

Для сохранения скриншотов установленным Google Chrome:

```powershell
npm install --prefix .tools/browser playwright@1.55.0
# Во время теста, примерно на 6:50:
node scripts/capture-grafana.cjs stress
# После завершения теста:
.\scripts\collect-results.ps1
```

Скриншоты сохраняются без редактирования изображения; JSON рядом содержит
время съёмки и точный интервал отображённых данных.

Основной тест длится 10 минут: рост до 50 VU, на 5–7-й минутах - задержка
700 ms у GET по ID и HTTP 500 у 25% запросов списка, затем восстановление.
Все данные на графиках должны поступать из реальных scrape. Для теста без
инъекции используется `-NoFaults`. Учебный режим включён отдельным values
файлом; по умолчанию приложение игнорирует fault-заголовки.

## Что находится в репозитории

```text
src/Blt.Users.Api/Observability/  HTTP-метрики и отключаемые учебные сбои
helm/blt-users-api/              чарт приложения из ДЗ № 4
infra/kind.yaml                  отдельный локальный кластер и порты
infra/nginx-ingress.yaml         ingress с включёнными метриками
infra/postgresql.yaml            PostgreSQL + postgres_exporter
monitoring/                     Prometheus, Grafana, RBAC, provisioning
loadtests/stress.js              k6: CRUD, нагрузка, инъекция и восстановление
scripts/                        запуск, генерация JSON и проверка данных
docs/monitoring.md               семантика метрик и воспроизведение
```

Подробности прежнего CRUD и raw-манифестов сохранены в
[описании ДЗ № 4](docs/homework-4.md). Их образ обновлён до `5.0.0`.
[Postman-коллекция](postman/homework-4.postman_collection.json) и
[OpenAPI](docs/openapi.yaml) сохраняют совместимость.

## Проверки и повторное развёртывание

```powershell
dotnet test --configuration Release
.tools/helm/windows-amd64/helm.exe lint helm/blt-users-api
python scripts/generate-dashboard.py
kubectl --kubeconfig .local/kubeconfig apply -k monitoring
```

Для существующего Kubernetes можно использовать те же манифесты и чарт,
обеспечив доступность образа приложения на его узлах. В Prometheus
фиксированы namespace `blt`, Service `blt-users-api` и ingress release
`ingress-nginx` в одноимённом namespace. Если имена отличаются, их нужно
изменить в конфигурации сбора и PromQL. При импорте JSON в свою Grafana
источник Prometheus должен иметь UID `prometheus`; алерты импортируются
отдельно от dashboard. Подробнее - [monitoring.md](docs/monitoring.md).

Остановить и удалить только этот учебный кластер (также удаляет его PVC):

```powershell
.tools/kind.exe delete cluster --name otus-hw5
```
