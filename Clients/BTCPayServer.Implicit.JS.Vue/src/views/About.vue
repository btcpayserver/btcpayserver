<template>
  <div class="about">
    <h1>This is a login-protected page</h1>
    <ul class="claims">
      <li v-for="c in claims"
          :key="c.key">
        <strong>{{c.key}}</strong>: {{c.value}}</li>
    </ul>
  </div>
</template>
<script>
export default {
  computed: {
    user() {
      return this.$oidc.user;
    },
    claims() {
      if (this.user) {
        return Object.keys(this.user).map(key => ({
          key,
          value: this.user[key]
        }));
      }
      return [];
    }
  }
};
</script>
<style>
.claims {
  list-style: none;
  text-align: left;
}
</style>
