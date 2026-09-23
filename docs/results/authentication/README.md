# Проверка ДЗ № 7

Проверено 23 сентября 2026 года (Europe/Moscow) в отдельном локальном
kind-кластере `otus-hw7`, Kubernetes 1.34.11. Приложение и PostgreSQL —
namespace `blt`, nginx-ingress — `ingress-nginx`.

| Проверка | Результат | Подтверждение |
|---|---|---|
| `dotnet test Blt.slnx --no-restore --nologo` | 29 пройдено, 0 ошибок, 0 пропущено | [dotnet-tests.txt](dotnet-tests.txt) |
| Сборка `vafomenko/blt-users-api:7.0.0`, linux/amd64 | Успешно, SQL встроен в сборку | [docker-build.txt](docker-build.txt) |
| Helm install и затем `./scripts/setup-auth.ps1 -SkipBuild` | Установка и повторное обновление успешны | [setup-output.txt](setup-output.txt) |
| Helm lint и server dry-run в namespace `blt` | Без ошибок; TLS отрендерен на обоих ingress | Проверены штатные шаблоны и вариант `ingress.tls` |
| `./scripts/test-auth.ps1 -BaseUrl http://127.0.0.1:8087` | 25 запросов, 80 assertions, 0 ошибок | [newman-output.txt](newman-output.txt) |
| Та же коллекция через `/otusapp/vyacheslav` | 25 запросов, 80 assertions, 0 ошибок | [newman-rewrite-output.txt](newman-rewrite-output.txt) |
| Готовность кластера | Две реплики API, PostgreSQL и Job миграций исправны | [cluster-check.txt](cluster-check.txt) |
| Миграции и очистка тестовых аккаунтов | Обе миграции применены; таблицы профилей, credentials и sessions пусты после cleanup | [database-check.json](database-check.json) |

В Newman проверены 401 до входа и после выхода, 403 при чтении, изменении
и удалении чужого профиля, сохранение собственных изменений и отсутствие
утечки через список пользователей. В логах CLI отображаются HTTP-запросы
и ответы; пароли и токены заменены на `[REDACTED]`.

.NET-тесты дополнительно используют настоящую временную PostgreSQL, проверяют
переход со схемы ДЗ № 6, идемпотентность миграций, конкурентную регистрацию,
хеширование и соль, истечение срока сессии, подделку токена, отзыв на другом
экземпляре API, подделку идентификатора через заголовки/JSON, все восемь
префиксов API, недоступность БД и лимит запросов к входу/регистрации.

Raw-манифесты проверены через `kubectl apply --dry-run=client -f k8s`.
Работающий стенд установлен через Helm. HTTPS проверен на уровне шаблонов;
локальный HTTP-порт 8087 доступен через loopback.
