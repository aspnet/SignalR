import { asyncit as it } from "./Utils"
import { TestHttpClient } from "./TestHttpClient";
import { HttpRequest } from "../Microsoft.AspNetCore.SignalR.Client.TS/index";

describe("HttpClient", () => {
    describe("get", () => {
        it("sets the method and URL appropriately", async () => {
            let request: HttpRequest;
            let testClient = new TestHttpClient().on(r => {
                request = r; return "";
            });

            await testClient.get("http://localhost");
            expect(request.method).toEqual("GET");
            expect(request.url).toEqual("http://localhost");
        });

        it("overrides method and url in options", async () => {
            let request: HttpRequest;
            let testClient = new TestHttpClient().on(r => {
                request = r; return "";
            });

            await testClient.get("http://localhost", {
                method: "OPTIONS",
                url: "http://wrong"
            });
            expect(request.method).toEqual("GET");
            expect(request.url).toEqual("http://localhost");
        })

        it("copies other options", async () => {
            let request: HttpRequest;
            let testClient = new TestHttpClient().on(r => {
                request = r; return "";
            });

            await testClient.get("http://localhost", {
                timeout: 42,
            });
            expect(request.timeout).toEqual(42);
        })
    });

    describe("post", () => {
        it("sets the method and URL appropriately", async () => {
            let request: HttpRequest;
            let testClient = new TestHttpClient().on(r => {
                request = r; return "";
            });

            await testClient.post("http://localhost");
            expect(request.method).toEqual("POST");
            expect(request.url).toEqual("http://localhost");
        });

        it("overrides method and url in options", async () => {
            let request: HttpRequest;
            let testClient = new TestHttpClient().on(r => {
                request = r; return "";
            });

            await testClient.post("http://localhost", {
                method: "OPTIONS",
                url: "http://wrong"
            });
            expect(request.method).toEqual("POST");
            expect(request.url).toEqual("http://localhost");
        })

        it("copies other options", async () => {
            let request: HttpRequest;
            let testClient = new TestHttpClient().on(r => {
                request = r; return "";
            });

            await testClient.post("http://localhost", {
                timeout: 42,
            });
            expect(request.timeout).toEqual(42);
        })
    });
});
