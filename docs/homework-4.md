# Домашнее задание № 4 - инфраструктурные паттерны

Ветка **homework-4** продолжает домашние работы 1–3. Реализован RESTful CRUD
пользователей на ASP.NET Core/.NET 10 с PostgreSQL, Kubernetes и Helm. Внешний
адрес приложения: **http://arch.homework**.

## Выполнение требований

| Требование | Реализация |
|---|---|
| Создание, просмотр, изменение, удаление пользователей | POST/GET/PUT/DELETE /user и /user/{userId}; числовой int64 ID, username, firstName, lastName, email, phone |
| База данных | PostgreSQL из Bitnami Helm-чарта; таблица users и таблица версий schema_migrations |
| ConfigMaps | k8s/01-configmap.yaml содержит URL, host, port и имя БД, без пароля |
| Secrets | k8s/02-secret.yaml хранит Database__Username и Database__Password; Helm поддерживает database.existingSecret |
| Первоначальная миграция | Job k8s/03-migration-job.yaml запускает приложение с аргументом --migrate |
| Ingress arch.homework/ | k8s/06-ingress-root.yaml направляет корневой URL; k8s/07-ingress-rewrite.yaml сохраняет маршрут /otusapp/vyacheslav/... из ДЗ № 3 |
| Задание со звездочкой | helm/blt-users-api - шаблоны Deployment, Service, Ingress, ConfigMap, Secret и Job |
| Postman/Newman | postman/homework-4.postman_collection.json: create, get, update, delete и проверка 404 |

Контракт соответствует примеру OTUS: ресурс называется /user, ID имеет формат
int64, а PUT может не содержать username. Для совместимости добавлены /users,
/v1/user и /v1/users.

## Ссылки для pull request

После публикации ветки создайте PR homework-4 -> main:

- [Kubernetes-манифесты](https://github.com/vafomenko/otus-homework/tree/homework-4/k8s)
- [Helm-чарт](https://github.com/vafomenko/otus-homework/tree/homework-4/helm/blt-users-api)
- [создать pull request](https://github.com/vafomenko/otus-homework/compare/main...homework-4?expand=1)

## Структура

~~~text
db/migrations/001_users.sql        эталон SQL-миграции
docs/openapi.yaml                  контракт OpenAPI 3
docs/newman-success.txt            пример успешного вывода Newman
infra/postgresql-values.yaml       values PostgreSQL
k8s/                               raw Kubernetes-манифесты
helm/blt-users-api/               Helm-чарт приложения
postman/homework-4.postman_collection.json
src/Blt.Users.Api/                приложение и migration runner
tests/Blt.Users.Api.Tests/        тесты
~~~

## Предварительные требования и Ingress

Нужны Docker, .NET SDK 10, kubectl, Helm 3, Minikube (или другой кластер с
NGINX Ingress) и Newman.

~~~powershell
minikube start
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx/
helm repo update
helm upgrade --install nginx ingress-nginx/ingress-nginx --namespace m --create-namespace --values infra/nginx-ingress.yaml
kubectl wait --namespace m --for=condition=Ready pod --selector=app.kubernetes.io/component=controller --timeout=180s
~~~

Добавьте IP Minikube в hosts:

~~~text
<minikube-ip> arch.homework
~~~

В Windows это C:\Windows\System32\drivers\etc\hosts, в Linux/macOS - /etc/hosts.

## Сборка образа

Манифесты используют vafomenko/blt-users-api:4.0.0:

~~~powershell
$Image = 'vafomenko/blt-users-api:4.0.0'
docker build --platform linux/amd64 --tag $Image .
docker image inspect $Image --format '{{.Os}}/{{.Architecture}}'
docker push $Image
~~~

При необходимости замените image в raw-манифестах или image.repository/image.tag
в values.yaml.

## Установка PostgreSQL из Helm

Файл значений: [infra/postgresql-values.yaml](../infra/postgresql-values.yaml).
Для учебного кластера используется service users-db, база users, пользователь
users и пароль change-me. Перед общим использованием пароль замените или
используйте auth.existingSecret.

~~~powershell
helm repo add bitnami https://charts.bitnami.com/bitnami
helm repo update
helm upgrade --install users-db bitnami/postgresql --namespace blt --create-namespace --values infra/postgresql-values.yaml
kubectl wait --namespace blt --for=condition=Ready pod --selector=app.kubernetes.io/instance=users-db --timeout=180s
kubectl get service -n blt users-db
~~~

## Миграция и raw-манифесты

Рекомендуемый запуск в правильном порядке:

~~~powershell
kubectl apply -f k8s/00-namespace.yaml
kubectl apply -f k8s/01-configmap.yaml -f k8s/02-secret.yaml
kubectl delete job blt-users-api-migrations -n blt --ignore-not-found
kubectl apply -f k8s/03-migration-job.yaml
kubectl wait --for=condition=complete job/blt-users-api-migrations --namespace blt --timeout=180s
kubectl apply -f k8s/04-deployment.yaml -f k8s/05-service.yaml -f k8s/06-ingress-root.yaml -f k8s/07-ingress-rewrite.yaml
kubectl rollout status deployment/blt-users-api -n blt --timeout=180s
~~~

Команда применения первоначальной миграции - kubectl apply -f
k8s/03-migration-job.yaml (после ConfigMap, Secret и установки PostgreSQL).
Job идемпотентен; для повторного запуска удалите завершившийся Job.

Требуемая в задании единая команда также поддерживается:

~~~powershell
kubectl apply -f k8s/
kubectl wait --for=condition=complete job/blt-users-api-migrations --namespace blt --timeout=180s
kubectl rollout status deployment/blt-users-api -n blt --timeout=180s
~~~

Нумерация 00–07 отражает зависимости. Подробная последовательность находится
в k8s/README.md.

## Helm-чарт (задание со звездочкой)

~~~powershell
helm lint helm/blt-users-api
helm template blt-users-api helm/blt-users-api --namespace blt
helm upgrade --install blt-users-api ./helm/blt-users-api --namespace blt --create-namespace
kubectl rollout status deployment/blt-users-api --namespace blt --timeout=180s
~~~

Migration Job чарта - Helm hook pre-install/pre-upgrade. Для production задайте
database.existingSecret в отдельном values-файле; Secret должен содержать ключи
Database__Username и Database__Password.

## API

~~~powershell
curl.exe -X POST http://arch.homework/user -H 'Content-Type: application/json' -d '{"username":"johndoe589","firstName":"John","lastName":"Doe","email":"john@example.com","phone":"+71002003040"}'
curl.exe http://arch.homework/user
curl.exe http://arch.homework/user/1
curl.exe -X PUT http://arch.homework/user/1 -H 'Content-Type: application/json' -d '{"firstName":"Julie","lastName":"Doe","email":"julie@example.com","phone":"+71004242424"}'
curl.exe -i -X DELETE http://arch.homework/user/1
~~~

Rewrite-проверка из предыдущего задания:

~~~powershell
curl.exe http://arch.homework/otusapp/vyacheslav/user/1
~~~

Ожидаемые коды: POST 201, GET 200, PUT 200, DELETE 204, неизвестный ID 404.
Ошибки имеют тело {"code": <число>, "message": "<текст>"}.

## Newman

Коллекция использует базовый URL http://arch.homework и сама сохраняет ID
созданного пользователя:

~~~powershell
newman run postman/homework-4.postman_collection.json
~~~

Она проверяет создание, получение, обновление, удаление и последующий 404.
Пример вывода с нулевым числом ошибок: [docs/newman-success.txt](../docs/newman-success.txt).

## Локальная проверка

~~~powershell
dotnet restore
dotnet test --configuration Release --no-restore
dotnet run --project src/Blt.Users.Api
curl.exe http://localhost:8000/health/
~~~

Health возвращает {"status":"OK"} и не требует БД. Для локального CRUD после
настройки Database:* запустите первоначальную миграцию:

~~~powershell
dotnet run --project src/Blt.Users.Api -- --migrate
~~~

Удаление:

~~~powershell
helm uninstall blt-users-api --namespace blt
kubectl delete -f k8s/ --ignore-not-found
helm uninstall users-db --namespace blt
~~~
