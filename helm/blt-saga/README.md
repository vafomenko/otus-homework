# blt-saga

ДЗ №9: Users, Orders с оркестрируемой Saga, Billing, Warehouse, Delivery,
Notifications, отдельная PostgreSQL для каждого сервиса и Kafka.

```powershell
kubectl create namespace blt --dry-run=client -o yaml | kubectl apply -f -
helm upgrade --install blt-saga ./helm/blt-saga --namespace blt --wait --wait-for-jobs --timeout 10m
```

Образы `9.0.0` должны быть собраны и доступны узлам. Чарт требует ingress
controller класса `nginx` и default StorageClass. Домен ingress — `arch.homework`.
Полная подготовка, локальный kind-стенд и тесты:
[docs/saga/README.md](../../docs/saga/README.md).
