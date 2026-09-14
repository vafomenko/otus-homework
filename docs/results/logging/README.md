# Проверка логирования и EFK

Проверено 14.09.2026 в учебном kind-кластере `otus-hw5` на Docker Desktop,
Kubernetes 1.34.11. API/PostgreSQL в `blt`, EFK в `logging`.

- [dotnet-tests.txt](dotnet-tests.txt): 8 тестов пройдены — контракт health,
  метрики/инъекция сбоев, JSON в одну строку, типизированный user_id,
  request scope и отсутствие текста exception с секретом.
- [http-checks.json](http-checks.json): 10 HTTP-проверок — POST/GET/PUT/DELETE,
  конфликт 409, невалидные поля и JSON 400, отсутствующий пользователь 404,
  управляемый 500. Все статусы совпали с ожидаемыми, тестовый пользователь удалён.
- [database-check.json](database-check.json) и [database-error.jsonl](database-error.jsonl):
  настоящий отказ подключения к БД в отдельном временном Pod → 503 + ERROR.
  Pod удалён после проверки; основная БД не останавливалась.
- [index-check.json](index-check.json): все 16 проверяемых типов событий найдены
  в Elasticsearch с нужными уровнями и метаданными Kubernetes. CRUD связан
  одним user_id; каждое событие CRUD содержит request_id.
- [event-samples.json](event-samples.json): реальные документы Elasticsearch,
  включая старт/остановку, CRUD, валидацию, отказ БД и Job миграций.
- [cluster-check.json](cluster-check.json): состояние EFK, PVC и API.
- [capture.json](capture.json): параметры браузерной съёмки.

В `capture.json` также сохранены ответы 404/500 служебных API Kibana
(`user_profile`, `streams`, `elastic_assistant`) при отключённой security.
Data Views, Discover и гистограмма проверены визуально; эти ответы
не мешают сбору логов и использованным страницам.

Helm lint пройден, EFK-манифесты прошли `kubectl apply --dry-run=server -k logging`.
Проверены также повторная установка `setup-logging.ps1 -SkipBuild`, штатный
rollout restart API и доставка накопленного дискового буфера Fluent Bit.

На этой машине DNS Docker не позволил напрямую получить часть образов.
Официальные образы были загружены через хост, проверены по SHA256 и перенесены
в kind. API собран `dotnet publish -c Release -r linux-x64 --self-contained false`
и упакован в runtime-образ `mcr.microsoft.com/dotnet/aspnet:10.0` как `6.0.0`.
Основной многостадийный Dockerfile сохранён; обычная команда установки использует
его при доступном registry. Образы не публиковались.
