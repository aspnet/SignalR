// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

const path = require("path");
const webpack = require("webpack");

module.exports = {
    entry: path.resolve(__dirname, "ts", "index.ts"),
    mode: "none",
    devtool: "source-map",
    node: {
        process: false,
    },
    module: {
        rules: [
            {
                test: /\.ts$/,
                use: [
                    {
                        loader: "ts-loader",
                        options: {
                            configFile: path.resolve(__dirname, "tsconfig.json"),
                        },
                    },
                ],
                exclude: /node_modules/,
            }
        ]
    },
    resolve: {
        extensions: [".ts", ".js"]
    },
    output: {
        filename: 'signalr-functional-tests.js',
        path: path.resolve(__dirname, "wwwroot", "dist"),
    },
    externals: {
        "@aspnet/signalr": "signalR",
        "@aspnet/signalr-protocols-msgpack": "signalR.protocols.msgpack",
    },
};