# Скриншоты Kibana — ДЗ № 6

Сняты с работающей Kibana 8.19.3 в Kubernetes 14.09.2026.
Оригинальные PNG получены Playwright через Google Chrome; изображения не редактировались.
В Kibana 8 раздел Index Patterns называется Data Views.

1. [Stack Management → Data Views](data-view.png): `fluent-bit-logs-*`, время `@timestamp`.
2. [Discover: события приложения](discover-events.png): фильтр по сервису, INFO/WARN/ERROR,
   колонки level/event/user_id/message/Pod, CRUD и жизненный цикл.
3. [Discover: WARN и ERROR](discover-errors.png): ошибки валидации, 404, HTTP 500 и настоящий отказ БД.
4. [Discover: Job миграций](discover-migrations.png): `migration_started`, `migration_completed` и имя Pod.
5. [Discover: конкретный пользователь](discover-user.png): поиск по `user_id`, создание/чтение/обновление/удаление.
6. [Гистограмма по уровням за последний час](log-levels.png): дашборд с графиком и таблицей событий.

Повторить: импортировать `python scripts/configure-kibana.py`, выполнить проверки из
[инструкции](../../logging.md), затем `node scripts/capture-kibana.cjs`.
Время и ID пользователя фиксируются в [capture.json](../../results/logging/capture.json).
