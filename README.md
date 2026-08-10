# OTUS Microservice Architecture — Домашнее задание № 3

## Основы работы с Kubernetes

Минимальный HTTP-сервис на C# и ASP.NET Core, развёрнутый в Kubernetes с
помощью `Deployment`, `Service` и NGINX `Ingress`.

Задание выполнено со звёздочкой: запросы с префиксом
`/otusapp/vyacheslav/` перенаправляются в сервис без этого префикса.

## Результат

| Артефакт | Значение |
|---|---|
| Основной URL | `http://arch.homework/health` |
| URL со звёздочкой | `http://arch.homework/otusapp/vyacheslav/health` |
| Docker-образ | [`vafomenko/blt-orders-api:1.0.0`](https://hub.docker.com/r/vafomenko/blt-orders-api) |
| Kubernetes-манифесты | [`k8s/`](k8s/) |
| Postman-коллекция | [`postman/homework-3.postman_collection.json`](postman/homework-3.postman_collection.json) |

Оба HTTP-запроса возвращают:

```json
{"status":"OK"}
```

## Соответствие требованиям

| Требование | Реализация |
|---|---|
| Сервис слушает порт `8000` | Kestrel слушает `0.0.0.0:8000` |
| `GET /health/` | ASP.NET Core Minimal API |
| Не менее двух реплик | `replicas: 2` в Deployment |
| Образ из Docker Hub | `vafomenko/blt-orders-api:1.0.0` |
| Liveness и Readiness | HTTP-пробы обращаются к `/health/` |
| Service | `ClusterIP`, порт `8000` |
| Ingress host | `arch.homework` |
| Ingress class | `ingressClassName: nginx` |
| Задание со звёздочкой | `/otusapp/vyacheslav(/|$)(.*)` переписывается в `/$2` |
| Применение одной командой | `kubectl apply -f k8s/` |
| Автоматическая HTTP-проверка | Два запроса в Postman/Newman |

## Схема запросов

```text
GET arch.homework/health
                    └──> NGINX Ingress ──> Service :8000 ──> один из 2 Pods

GET arch.homework/otusapp/vyacheslav/health
                    └──> rewrite /health ──> Service :8000 ──> один из 2 Pods
```

Два ресурса Ingress разделяют обычный маршрут и маршрут с rewrite. Это не
позволяет аннотации `rewrite-target` изменить основной `/health` endpoint.

## Структура проекта

```text
.
├── infra/
│   └── nginx-ingress.yaml
├── k8s/
│   ├── deployment.yaml
│   ├── ingress.yaml
│   └── service.yaml
├── postman/
│   └── homework-3.postman_collection.json
├── src/Blt.Orders.Api/
├── tests/Blt.Orders.Api.Tests/
├── Dockerfile
└── README.md
```

## Локальная проверка приложения

Требуется .NET SDK 10.

```powershell
dotnet restore
dotnet test --configuration Release --no-restore
dotnet run --project src/Blt.Orders.Api
```

В другом терминале:

```powershell
curl.exe http://localhost:8000/health
```

## Сборка и публикация Docker-образа

Образ собирается для `linux/amd64`, поэтому он запускается и на Minikube с
обычной x86-64 нодой, даже если сборка выполняется на Apple Silicon.

```powershell
$Image = 'vafomenko/blt-orders-api:1.0.0'

docker build --platform linux/amd64 --tag $Image .
docker image inspect $Image --format '{{.Os}}/{{.Architecture}}'
docker push $Image
```

## Развёртывание в Minikube

Требуются Docker, Minikube, `kubectl` и Helm.

### 1. Запустить кластер

```powershell
minikube start
```

### 2. Установить NGINX Ingress Controller через Helm

Контроллер устанавливается отдельно от приложения. Файл
`infra/nginx-ingress.yaml` включает `hostPort` и запускает контроллер как
`DaemonSet`, чтобы `arch.homework` был доступен на порту `80` IP-адреса
Minikube.

```powershell
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx/
helm repo update
helm upgrade --install nginx ingress-nginx/ingress-nginx `
  --namespace m `
  --create-namespace `
  --values infra/nginx-ingress.yaml

kubectl wait --namespace m `
  --for=condition=Ready pod `
  --selector=app.kubernetes.io/component=controller `
  --timeout=180s
```

### 3. Настроить имя хоста

Получить адрес кластера:

```powershell
minikube ip
```

Добавить в файл hosts строку, подставив полученный адрес:

```text
<minikube-ip> arch.homework
```

В Windows файл находится по пути
`C:\Windows\System32\drivers\etc\hosts` и редактируется от имени
администратора. В Linux и macOS используется `/etc/hosts`.

### 4. Применить все манифесты одной командой

```powershell
kubectl apply -f k8s/
kubectl rollout status deployment/blt-orders-api --timeout=120s
kubectl get deployment,pods,service,ingress
```

## Проверка результата

Прямой маршрут:

```powershell
curl.exe http://arch.homework/health
```

Маршрут со звёздочкой и rewrite:

```powershell
curl.exe http://arch.homework/otusapp/vyacheslav/health
```

Ожидаемый ответ в обоих случаях:

```json
{"status":"OK"}
```

Проверка коллекции через Newman:

```powershell
newman run postman/homework-3.postman_collection.json
```

Коллекция проверяет HTTP-код `200`, `Content-Type` и точное JSON-тело для
обоих маршрутов.

## Удаление

Удалить приложение:

```powershell
kubectl delete -f k8s/
```

При необходимости полностью удалить установленный контроллер и локальный
кластер:

```powershell
helm uninstall nginx --namespace m
kubectl delete namespace m
minikube delete
```

## Диагностика

```powershell
kubectl describe deployment blt-orders-api
kubectl describe ingress blt-orders-api
kubectl describe ingress blt-orders-api-rewrite
kubectl logs deployment/blt-orders-api
kubectl get endpointslices -l kubernetes.io/service-name=blt-orders-api
```
