module.exports = {
  chainWebpack: config => {
    config.resolve.set('symlinks', false);
  }
};
