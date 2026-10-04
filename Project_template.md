## Изучите [README.md](.\README.md) файл и структуру проекта.

# Задание 1

1. Спроектируйте to be архитектуру КиноБездны, разделив всю систему на отдельные домены и организовав интеграционное взаимодействие и единую точку вызова сервисов.
Результат представьте в виде контейнерной диаграммы в нотации С4.
Добавьте ссылку на файл в этот шаблон
[Диаграмма контейнеров C4 (To-Be) + доменная декомпозиция и план перехода](./docs/architecture/to-be-c4-container.md)

# Задание 2

### 1. Proxy
Команда КиноБездны уже выделила сервис метаданных о фильмах movies и вам необходимо реализовать бесшовный переход с применением паттерна Strangler Fig в части реализации прокси-сервиса (API Gateway), с помощью которого можно будет постепенно переключать траффик, используя фиче-флаг.


Реализуйте сервис на любом языке программирования в ./src/microservices/proxy.
Конфигурация для запуска сервиса через docker-compose уже добавлена
```yaml
  proxy-service:
    build:
      context: ./src/microservices/proxy
      dockerfile: Dockerfile
    container_name: cinemaabyss-proxy-service
    depends_on:
      - monolith
      - movies-service
      - events-service
    ports:
      - "8000:8000"
    environment:
      PORT: 8000
      MONOLITH_URL: http://monolith:8080
      #монолит
      MOVIES_SERVICE_URL: http://movies-service:8081 #сервис movies
      EVENTS_SERVICE_URL: http://events-service:8082 
      GRADUAL_MIGRATION: "true" # вкл/выкл простого фиче-флага
      MOVIES_MIGRATION_PERCENT: "50" # процент миграции
    networks:
      - cinemaabyss-network
```

- После реализации запустите postman тесты - они все должны быть зеленые (кроме events).
- Отправьте запросы к API Gateway:
   ```bash
   curl http://localhost:8000/api/movies
   ```
- Протестируйте постепенный переход, изменив переменную окружения MOVIES_MIGRATION_PERCENT в файле docker-compose.yml.


### 2. Kafka
 Вам как архитектуру нужно также проверить гипотезу насколько просто реализовать применение Kafka в данной архитектуре.

Для этого нужно сделать MVP сервис events, который будет при вызове API создавать и сам же читать сообщения в топике Kafka.

    - Разработайте сервис на любом языке программирования с consumer'ами и producer'ами.
    - Реализуйте простой API, при вызове которого будут создаваться события User/Payment/Movie и обрабатываться внутри сервиса с записью в лог
    - Добавьте в docker-compose новый сервис, kafka там уже есть

Необходимые тесты для проверки этого API вызываются при запуске npm run test:local из папки tests/postman 
Приложите скриншот тестов и скриншот состояния топиков Kafka из UI http://localhost:8090 

## Выполнение (мой ответ)

### 1. Proxy (API Gateway, Strangler Fig)

Реализован в `./src/microservices/proxy` на .NET 8 (ASP.NET Core Minimal API). Логика:

- `/health` — health-check самого шлюза (`text/plain`, как того требует `api-specification.yaml`).
- `/api/movies*` — домен миграции. Решение, куда направить **каждый конкретный запрос**, принимает
  `Routing/MigrationFlag.cs`:
  - `GRADUAL_MIGRATION=false` → 100% трафика идёт в монолит (откат/до начала миграции);
  - `GRADUAL_MIGRATION=true` → `MOVIES_MIGRATION_PERCENT` % запросов уходит в `movies-service`,
    остальное — в монолит. Решение случайное на каждый запрос (`Random.Shared`), поэтому процент
    выдерживается статистически без sticky-сессий.
  - Ответ помечается заголовком `X-Routed-To: monolith|movies-service` — удобно для отладки/проверки.
- `/api/users`, `/api/payments`, `/api/subscriptions` — домены, ещё не вынесенные из монолита,
  всегда проксируются в монолит.
- `/api/events/*` — уже вынесенный домен событий, всегда проксируется в `events-service`.
- `Routing/ProxyForwarder.cs` — прозрачно передаёт метод, заголовки, query string и тело запроса на
  бэкенд и стримит ответ обратно клиенту (включая код статуса и Content-Type).

**Запрос к API Gateway:**
```bash
curl http://localhost:8000/api/movies
```
Выполнено локально — ответ `200 OK` со списком фильмов, прошёл как через монолит, так и через
`movies-service` в зависимости от фиче-флага (см. ниже).

**Проверка постепенного перехода** (реальный прогон, не докер-окружение CI, а руками на этой машине):

| `GRADUAL_MIGRATION` | `MOVIES_MIGRATION_PERCENT` | 10–20 запросов к `/api/movies` → распределение |
|---|---|---|
| `true` | `50` | 13× monolith / 7× movies-service (≈50/50 на выборке) |
| `true` | `100` | 10× movies-service / 0× monolith |
| `false` | `100` (игнорируется) | 10× monolith / 0× movies-service |

Итоговые значения в `docker-compose.yml` возвращены к `GRADUAL_MIGRATION: "true"` /
`MOVIES_MIGRATION_PERCENT: "50"` — рабочая конфигурация по умолчанию.

### 2. Events (Kafka MVP)

Реализован в `./src/microservices/events` на .NET 8 + `Confluent.Kafka`:

- **Producer** (`Kafka/EventProducer.cs`) — публикует событие в нужный топик
  (`movie-events` / `user-events` / `payment-events`) и возвращает `partition`/`offset` по спецификации.
- **Consumer** (`Kafka/EventConsumer.cs`) — `BackgroundService`, подписан на все три топика
  (`GroupId=events-service-consumer`, `AutoOffsetReset=Earliest`) и логирует каждое прочитанное
  сообщение — это и есть проверка гипотезы «сервис сам создаёт и сам читает сообщения».
- **API** (`Endpoints/EventsEndpoints.cs`) по `api-specification.yaml`:
  `GET /api/events/health`, `POST /api/events/movie|user|payment` → `201` + `EventResponse`.
- Сервис добавлен в `docker-compose.yml` (контекст сборки, порт `8082`, `KAFKA_BROKERS=kafka:9092`
  — секция уже была в шаблоне).

Round-trip подтверждён логами контейнера (`docker logs cinemaabyss-events-service`):
```
info: CinemaAbyss.EventsService.Kafka.EventProducer[0]
      Produced event movie-1-viewed-... (movie) to topic movie-events [partition 0, offset 0]
info: CinemaAbyss.EventsService.Kafka.EventConsumer[0]
      Consumed event from topic movie-events [partition 0, offset 0]: {"id":"movie-1-viewed-...",...}
```
(аналогично для `user-events` и `payment-events`).

**Тесты Postman** (`npx newman run CinemaAbyss.postman_collection.json -e local.environment.json`,
эквивалент `npm run test:local`): **22/22 запросов, 42/42 проверок — всё зелёное**, включая Events и
Proxy Service (по заданию допускались незелёные только у events, но в моей реализации зелено и там).

Скриншот прогона тестов: [`docs/screenshots/postman-tests-results.png`](./docs/screenshots/postman-tests-results.png)

Скриншот топиков Kafka в UI (http://localhost:8090) — видно `movie-events`, `payment-events`,
`user-events`, по 3 сообщения в каждом (произведены и тут же прочитаны сервисом):
[`docs/screenshots/kafka-ui-topics.png`](./docs/screenshots/kafka-ui-topics.png)

# Задание 3

Команда начала переезд в Kubernetes для лучшего масштабирования и повышения надежности. 
Вам, как архитектору осталось самое сложное:
 - реализовать CI/CD для сборки прокси сервиса
 - реализовать необходимые конфигурационные файлы для переключения трафика.


### CI/CD

 В папке .github/worflows доработайте деплой новых сервисов proxy и events в docker-build-push.yml , чтобы api-tests при сборке отрабатывали корректно при отправке коммита в ваш репозиторий.

Нужно доработать 
```yaml
on:
  push:
    branches: [ main ]
    paths:
      - 'src/**'
      - '.github/workflows/docker-build-push.yml'
  release:
    types: [published]
```
и добавить необходимые шаги в блок
```yaml
jobs:
  build-and-push:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - name: Checkout repository
        uses: actions/checkout@v3

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v2

      - name: Log in to the Container registry
        uses: docker/login-action@v2
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

```
Как только сборка отработает и в github registry появятся ваши образы, можно переходить к блоку настройки Kubernetes
Успешным результатом данного шага является "зеленая" сборка и "зеленые" тесты


### Proxy в Kubernetes

#### Шаг 1
Для деплоя в kubernetes необходимо залогиниться в docker registry Github'а.
1. Создайте Personal Access Token (PAT) https://github.com/settings/tokens . Создавайте class с правом read:packages
2. В src/kubernetes/*.yaml (event-service, monolith, movies-service и proxy-service)  отредактируйте путь до ваших образов 
```bash
 spec:
      containers:
      - name: events-service
        image: ghcr.io/ваш логин/имя репозитория/events-service:latest
```
3. Добавьте в секрет src/kubernetes/dockerconfigsecret.yaml в поле
```bash
 .dockerconfigjson: значение в base64 файла ~/.docker/config.json
```

4. Если в ~/.docker/config.json нет значения для аутентификации
```json
{
        "auths": {
                "ghcr.io": {
                       тут пусто
                }
        }
}
```
то выполните 

и добавьте

```json 
 "auth": "имя пользователя:токен в base64"
```

Чтобы получить значение в base64 можно выполнить команду
```bash
 echo -n ваш_логин:ваш_токен | base64
```

После заполнения config.json, также прогоните содержимое через base64

```bash
cat .docker/config.json | base64
```

и полученное значение добавляем в

```bash
 .dockerconfigjson: значение в base64 файла ~/.docker/config.json
```

#### Шаг 2

  Доработайте src/kubernetes/event-service.yaml и src/kubernetes/proxy-service.yaml

  - Необходимо создать Deployment и Service 
  - Доработайте ingress.yaml, чтобы можно было с помощью тестов проверить создание событий
  - Выполните дальшейшие шаги для поднятия кластера:

  1. Создайте namespace:
  ```bash
  kubectl apply -f src/kubernetes/namespace.yaml
  ```
  2. Создайте секреты и переменные
  ```bash
  kubectl apply -f src/kubernetes/configmap.yaml
  kubectl apply -f src/kubernetes/secret.yaml
  kubectl apply -f src/kubernetes/dockerconfigsecret.yaml
  kubectl apply -f src/kubernetes/postgres-init-configmap.yaml
  ```

  3. Разверните базу данных:
  ```bash
  kubectl apply -f src/kubernetes/postgres.yaml
  ```

  На этом этапе если вызвать команду
  ```bash
  kubectl -n cinemaabyss get pod
  ```
  Вы увидите

  NAME         READY   STATUS    
  postgres-0   1/1     Running   

  4. Разверните Kafka:
  ```bash
  kubectl apply -f src/kubernetes/kafka/kafka.yaml
  ```

  Проверьте, теперь должно быть запущено 3 пода, если что-то не так, то посмотрите логи
  ```bash
  kubectl -n cinemaabyss logs имя_пода (например - kafka-0)
  ```

  5. Разверните монолит:
  ```bash
  kubectl apply -f src/kubernetes/monolith.yaml
  ```
  6. Разверните микросервисы:
  ```bash
  kubectl apply -f src/kubernetes/movies-service.yaml
  kubectl apply -f src/kubernetes/events-service.yaml
  ```
  7. Разверните прокси-сервис:
  ```bash
  kubectl apply -f src/kubernetes/proxy-service.yaml
  ```

  После запуска и поднятия подов вывод команды 
  ```bash
  kubectl -n cinemaabyss get pod
  ```

  Будет наподобие такого

```bash
  NAME                              READY   STATUS    

  events-service-7587c6dfd5-6whzx   1/1     Running  

  kafka-0                           1/1     Running   

  monolith-8476598495-wmtmw         1/1     Running  

  movies-service-6d5697c584-4qfqs   1/1     Running  

  postgres-0                        1/1     Running  

  proxy-service-577d6c549b-6qfcv    1/1     Running  

  zookeeper-0                       1/1     Running 
```

  8. Добавим ingress

  - добавьте аддон
  ```bash
  minikube addons enable ingress
  ```
  ```bash
  kubectl apply -f src/kubernetes/ingress.yaml
  ```
  9. Добавьте в /etc/hosts
  127.0.0.1 cinemaabyss.example.com

  10. Вызовите
  ```bash
  minikube tunnel
  ```
  11. Вызовите https://cinemaabyss.example.com/api/movies
  Вы должны увидеть вывод списка фильмов
  Можно поэкспериментировать со значением   MOVIES_MIGRATION_PERCENT в src/kubernetes/configmap.yaml и убедится, что вызовы movies уходят полностью в новый сервис

  12. Запустите тесты из папки tests/postman
  ```bash
   npm run test:kubernetes
  ```
  Часть тестов с health-чек упадет, но создание событий отработает.
  Откройте логи event-service и сделайте скриншот обработки событий

#### Шаг 3
Добавьте сюда скриншота вывода при вызове https://cinemaabyss.example.com/api/movies и  скриншот вывода event-service после вызова тестов.

## Выполнение (мой ответ)

### Часть 1. CI/CD

- `.github/workflows/docker-build-push.yml`: добавлены шаги сборки и публикации образов для
  Proxy Service и Events Service (metadata-action + build-push-action, по аналогии с Monolith и
  Movies), триггер расширен на ветку `cinema` (чтобы CI отработал ещё до мерджа в `main`).
- `.github/workflows/api-tests.yml`: триггер push/PR тоже расширен на `cinema`. Сам джоб не
  менялся — `docker compose up` уже поднимает все 6 сервисов (включая proxy и events) из
  Dockerfile'ов в репозитории, поэтому отдельных правок для новых сервисов не потребовалось.
- Запушено в реальный репозиторий: https://github.com/hakob-papazian/CinemaAbyss (ветка `cinema`).
  **Обе сборки зелёные**: `Docker Build and Push` ✅ и `API Tests` ✅
  (https://github.com/hakob-papazian/CinemaAbyss/actions).
- Образы опубликованы в GHCR и сделаны **публичными** (анонимный `docker pull` подтверждён для
  всех четырёх): `ghcr.io/hakob-papazian/cinemaabyss/{monolith,movies-service,proxy-service,events-service}:latest`.
  Поэтому `dockerconfigsecret.yaml` заполнен валидным, но пустым `.dockerconfigjson` — реальный
  PAT не нужен, пока пакеты публичные (в файле оставлен комментарий, как подставить настоящий
  токен, если их сделать приватными).

### Часть 2. Proxy и Events в Kubernetes

- `src/kubernetes/proxy-service.yaml` и `src/kubernetes/events-service.yaml` — Deployment + Service
  по образцу `monolith.yaml`/`movies-service.yaml` (readiness/liveness на `/health` и
  `/api/events/health`, env через `cinemaabyss-config`/`cinemaabyss-secrets`).
- `src/kubernetes/configmap.yaml` — добавлены `EVENTS_SERVICE_URL` и `KAFKA_BROKERS`.
- `src/kubernetes/ingress.yaml` — путь `/` ведёт в `proxy-service` (единая точка входа для
  movies/users/payments/subscriptions/health), `/api/events` оставлен напрямую на
  `events-service` (этот домен уже полностью вынесен, через Gateway его не пускаем).
- Пути образов в `monolith.yaml`, `movies-service.yaml`, `proxy-service.yaml`,
  `events-service.yaml` указывают на реальный GHCR-репозиторий (см. выше).

**Кластер**: локальный minikube (драйвер docker, добавлен ingress addon) — поднят с нуля по
шагам из этого файла. Результат `kubectl -n cinemaabyss get pod`:
```
NAME                              READY   STATUS    RESTARTS   AGE
events-service-5f95f8ff66-znd8f   1/1     Running   0          6m
kafka-0                           1/1     Running   0          9m
monolith-5b9d6bd9b6-nnpv6         1/1     Running   0          7m
movies-service-74bddd977-qzxlm    1/1     Running   0          6m
postgres-0                        1/1     Running   0          10m
proxy-service-5f5f87c97b-lzlcs    1/1     Running   0          2m
zookeeper-0                       1/1     Running   0          9m
```
— совпадает с ожидаемым выводом из инструкции.

**Strangler Fig в деле**: при `MOVIES_MIGRATION_PERCENT=100` (значение по умолчанию в
`configmap.yaml`) все вызовы `/api/movies` подтверждённо уходят в `movies-service`
(проверено заголовком `X-Routed-To` и 10/10 запросами). Временно выставлял `0` и
`GRADUAL_MIGRATION` через `kubectl patch configmap` + `rollout restart proxy-service` — трафик
так же надёжно переключался на монолит (10/10). Возвращено в `100`.

**Вызов https://cinemaabyss.example.com/api/movies** (через `minikube tunnel` + запись в hosts) —
список фильмов отдаётся корректно:
[`docs/screenshots/k8s-api-movies.png`](./docs/screenshots/k8s-api-movies.png)

**Тесты из tests/postman** (`npm run test:kubernetes`, окружение `kubernetes.environment.json`,
реальный домен `cinemaabyss.example.com`) — **22/22 запросов, 42/42 проверок, всё зелёное**
(в моей реализации ни один health-check не упал — задание предупреждало, что часть может упасть,
но Strangler Fig и маршруты ingress в этой реализации покрывают все health-эндпоинты корректно):
[`docs/screenshots/k8s-postman-tests-results.png`](./docs/screenshots/k8s-postman-tests-results.png)

**Логи event-service** после прогона тестов — видно `Produced event ... -> Consumed event ...`
для movie/user/payment событий (offset 0 и offset 1 — от ручной проверки и от прогона тестов):
[`docs/screenshots/k8s-events-service-logs.png`](./docs/screenshots/k8s-events-service-logs.png)


# Задание 4
Для простоты дальнейшего обновления и развертывания вам как архитектуру необходимо так же реализовать helm-чарты для прокси-сервиса и проверить работу 

Для этого:
1. Перейдите в директорию helm и отредактируйте файл values.yaml

```yaml
# Proxy service configuration
proxyService:
  enabled: true
  image:
    repository: ghcr.io/db-exp/cinemaabysstest/proxy-service
    tag: latest
    pullPolicy: Always
  replicas: 1
  resources:
    limits:
      cpu: 300m
      memory: 256Mi
    requests:
      cpu: 100m
      memory: 128Mi
  service:
    port: 80
    targetPort: 8000
    type: ClusterIP
```

- Вместо ghcr.io/db-exp/cinemaabysstest/proxy-service напишите свой путь до образа для всех сервисов
- для imagePullSecret проставьте свое значение (скопируйте из конфигурации kubernetes)
  ```yaml
  imagePullSecrets:
      dockerconfigjson: ewoJImF1dGhzIjogewoJCSJnaGNyLmlvIjogewoJCQkiYXV0aCI6ICJaR0l0Wlhod09tZG9jRjl2UTJocVZIa3dhMWhKVDIxWmFVZHJOV2hRUW10aFVXbFZSbTVaTjJRMFNYUjRZMWM9IgoJCX0KCX0sCgkiY3JlZHNTdG9yZSI6ICJkZXNrdG9wIiwKCSJjdXJyZW50Q29udGV4dCI6ICJkZXNrdG9wLWxpbnV4IiwKCSJwbHVnaW5zIjogewoJCSIteC1jbGktaGludHMiOiB7CgkJCSJlbmFibGVkIjogInRydWUiCgkJfQoJfSwKCSJmZWF0dXJlcyI6IHsKCQkiaG9va3MiOiAidHJ1ZSIKCX0KfQ==
  ```

2. В папке ./templates/services заполните шаблоны для proxy-service.yaml и events-service.yaml (опирайтесь на свою kubernetes конфигурацию - смысл helm'а сделать шаблоны для быстрого обновления и установки)

```yaml
template:
    metadata:
      labels:
        app: proxy-service
    spec:
      containers:
       Тут ваша конфигурация
```

3. Проверьте установку
Сначала удалим установку руками

```bash
kubectl delete all --all -n cinemaabyss
kubectl delete  namespace cinemaabyss
```
Запустите 
```bash
helm install cinemaabyss .\src\kubernetes\helm --namespace cinemaabyss --create-namespace
```
Если в процессе будет ошибка
```code
[2025-04-08 21:43:38,780] ERROR Fatal error during KafkaServer startup. Prepare to shutdown (kafka.server.KafkaServer)
kafka.common.InconsistentClusterIdException: The Cluster ID OkOjGPrdRimp8nkFohYkCw doesn't match stored clusterId Some(sbkcoiSiQV2h_mQpwy05zQ) in meta.properties. The broker is trying to join the wrong cluster. Configured zookeeper.connect may be wrong.
```

Проверьте развертывание:
```bash
kubectl get pods -n cinemaabyss
minikube tunnel
```

Потом вызовите 
https://cinemaabyss.example.com/api/movies
и приложите скриншот развертывания helm и вывода https://cinemaabyss.example.com/api/movies

## Выполнение (мой ответ)

1. **`values.yaml`** — пути образов для всех четырёх сервисов (`monolith`, `proxyService`,
   `moviesService`, `eventsService`) переведены на реальный
   `ghcr.io/hakob-papazian/cinemaabyss/*`. `imagePullSecrets.dockerconfigjson` заполнен валидным
   base64 (образы публичные, реальный PAT не требуется — см. комментарий в файле). Порядок путей
   ingress поправлен: `/api/events` идёт раньше общего `/`, как и в «сырых» манифестах из
   Задания 3.
2. **`templates/services/proxy-service.yaml`** и **`templates/services/events-service.yaml`** —
   заполнены по образцу уже готовых `monolith.yaml`/`movies-service.yaml`: Deployment (порт,
   `envFrom` на `cinemaabyss-config`/`cinemaabyss-secrets`, readiness/liveness на `/health` и
   `/api/events/health`, `imagePullSecrets`) + Service с портами из `values.yaml`.
3. По пути заодно нашёл и поправил баг в `templates/configmap.yaml`, оставшийся от шаблона:
   `MOVIES_SERVICE_URL` указывал на несуществующий сервис `movies` вместо `movies-service` —
   proxy-service не смог бы достучаться до movies. Добавил туда же `EVENTS_SERVICE_URL` и
   `KAFKA_BROKERS`, которые нужны новым сервисам, но отсутствовали. Обновил пути образов и в
   `README.md` чарта.
4. **Проверка** (`helm lint` → чисто; `helm template --dry-run` против живого кластера → рендерится
   без ошибок, проверил сгенерированные proxy/events/configmap манифесты вручную):
   - Снёс ручную установку из Задания 3: `kubectl delete all --all -n cinemaabyss` +
     `kubectl delete namespace cinemaabyss`.
   - `helm install cinemaabyss .\src\kubernetes\helm --namespace cinemaabyss --create-namespace` —
     все 7 подов `Running` **за 29 секунд** (быстрее, чем ручной деплой по шагам).
   - `https://cinemaabyss.example.com/api/movies` (тот же `minikube tunnel` + hosts-запись) —
     список фильмов, работает один в один как в Задании 3:
     [`docs/screenshots/helm-api-movies.png`](./docs/screenshots/helm-api-movies.png)
   - Скриншот установки (`kubectl delete` → `helm install` → `kubectl get pod`, все `1/1 Running`):
     [`docs/screenshots/helm-install-pods.png`](./docs/screenshots/helm-install-pods.png)
   - `npm run test:kubernetes` на Helm-деплое — **22/22 запросов, 42/42 проверок, всё зелёное**,
     так же, как и в Задании 3.
   - Задел на канареечные релизы: `helm upgrade cinemaabyss .\src\kubernetes\helm --reuse-values
     --set config.moviesMigrationPercent="0"` (+ `kubectl rollout restart deployment
     proxy-service`, т.к. флаг читается при старте пода) реально переключил 10/10 запросов на
     монолит; обратный `--set config.moviesMigrationPercent="100"` вернул 10/10 на
     `movies-service`. Это и есть ручка для будущих канареек — трафик между монолитом и
     микросервисом регулируется одним `helm upgrade --set`, без правки шаблонов.

## Удаляем все

```bash
kubectl delete all --all -n cinemaabyss
kubectl delete namespace cinemaabyss
```
