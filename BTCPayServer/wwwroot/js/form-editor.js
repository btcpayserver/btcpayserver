document.addEventListener('DOMContentLoaded', () => {
    const parseConfig = str => {
        try {
            return JSON.parse(str)
        } catch (err) {
            console.error('Error deserializing form config:', err)
        }
    }
    const $config = document.getElementById('FormConfig')
    let config = parseConfig($config.value) || {}

    Vue.use(vSortable)

    new Vue({
        el: '#FormEditor',
        data () {
            return {
                config
            }
        },
        computed: {
            fields() {
                return this.config.fields || []
            },
            configJSON() {
                return JSON.stringify(this.config, null, 2)
            }
        },
        methods: {
            applyTemplate(id) {
                const $template = document.getElementById(`form-template-${id}`)
                this.config = JSON.parse($template.innerHTML.trim())
            },
            removeField(index) {
                this.config.fields.splice(index, 1)
            },
            sortFields(event) {
                const { newIndex, oldIndex } = event
                this.config.fields.splice(newIndex, 0, this.config.fields.splice(oldIndex, 1)[0])
            },
            updateFromJSON(event) {
                const config = parseConfig(event.target.value)
                if (config) this.config = config
            }
        }
    })
})
