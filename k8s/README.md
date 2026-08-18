# Kubernetes-манифесты

Файлы пронумерованы по зависимостям: namespace, ConfigMap/Secret, Job
миграции, затем Deployment, Service и Ingress.

## Рекомендуемый порядок

~~~powershell
kubectl apply -f k8s/00-namespace.yaml
kubectl apply -f k8s/01-configmap.yaml -f k8s/02-secret.yaml
kubectl delete job blt-users-api-migrations -n blt --ignore-not-found
kubectl apply -f k8s/03-migration-job.yaml
kubectl wait --for=condition=complete job/blt-users-api-migrations -n blt --timeout=180s
kubectl apply -f k8s/04-deployment.yaml -f k8s/05-service.yaml -f k8s/06-ingress-root.yaml -f k8s/07-ingress-rewrite.yaml
~~~

Для проверки сдачи можно применить каталог одной командой:

~~~powershell
kubectl apply -f k8s/
~~~

После неё дождитесь завершения Job и rollout Deployment. При повторной миграции
сначала удалите завершившийся Job.
