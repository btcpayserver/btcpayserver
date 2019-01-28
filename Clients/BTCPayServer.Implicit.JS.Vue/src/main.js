// import 'babel-polyfill';
import 'vue-oidc-client/src/polyfill';
import Vue from 'vue';
import App from './App.vue';
import mainAuth from './auth';
import router from './router';

Vue.config.productionTip = false;

mainAuth.startup().then(ok => {
  if (ok) {
    new Vue({
      router,
      render: h => h(App)
    }).$mount('#app');
  }
});
