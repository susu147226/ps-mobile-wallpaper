const path = require("path");
const CopyWebpackPlugin = require("copy-webpack-plugin");

// UXP injects these modules at runtime; they must not be bundled.
const uxpExternals = {
  photoshop: "commonjs2 photoshop",
  uxp: "commonjs2 uxp",
  fs: "commonjs2 fs",
  os: "commonjs2 os",
  path: "commonjs2 path",
};

module.exports = {
  target: "web",
  entry: "./src/main.ts",
  output: {
    path: path.resolve(__dirname, "dist"),
    filename: "main.js",
    clean: true,
  },
  resolve: {
    extensions: [".ts", ".js"],
  },
  module: {
    rules: [
      {
        test: /\.ts$/,
        use: "ts-loader",
        exclude: /node_modules/,
      },
    ],
  },
  externals: uxpExternals,
  plugins: [
    new CopyWebpackPlugin({
      patterns: [
        { from: "manifest.json", to: "manifest.json" },
        { from: "src/index.html", to: "index.html" },
        { from: "src/styles.css", to: "styles.css" },
      ],
    }),
  ],
  // UXP has no Node built-ins and no eval; a readable bundle aids debugging in UDT.
  devtool: false,
  optimization: {
    minimize: false,
  },
  performance: {
    hints: false,
  },
};
