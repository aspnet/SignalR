docker run --rm -d --name cursor-test-6001 -e "REDIS_CONNECTION=cursor-test-redis" --network cursor-test -e "ASPNETCORE_URLS=http://0.0.0.0:5000" -p 6001:5000 cursor-test-server
docker run --rm -d --name cursor-test-6002 -e "REDIS_CONNECTION=cursor-test-redis" --network cursor-test -e "ASPNETCORE_URLS=http://0.0.0.0:5000" -p 6002:5000 cursor-test-server
