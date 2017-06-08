var webpack = require('webpack'),
  path = require('path'),
  yargs = require('yargs');

var libraryName = 'signalr-client',
  plugins = [],
  outputFile;

if (yargs.argv.p) {
  plugins.push(new webpack.optimize.UglifyJsPlugin({ minimize: true }));
  outputFile = 'signalr-client.min.js';
} else {
  outputFile = 'signalr-client.js';
}

var config = {
  entry: {
    main: __dirname + '/src/index.ts'
  },
  devtool: 'source-map',
  output: {
    path: path.join(__dirname, '/dist'),
    filename: outputFile,
    library: libraryName,
    libraryTarget: 'umd',
    umdNamedDefine: true
  },
  module: {
    rules: [
      {
        test: /\.ts$/,
        exclude: /node_modules/,
        use: [
          'ts-loader'
        ]
      },
    ]
  },
  resolve: {
    extensions: ['*', '.js', '.json', '.ts'],
    modules: ['node_modules', path.resolve('./src')]
  },

  plugins: plugins
};

module.exports = config;
