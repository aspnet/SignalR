docker network create cursor-test

docker run -d --name cursor-test-redis --network cursor-test redis

# TODO: Install Kafka
