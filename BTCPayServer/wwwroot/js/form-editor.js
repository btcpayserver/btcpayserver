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

    const getFieldComponent = type =>
        ['fieldset', 'textarea', 'select'].includes(type)
            ? `field-type-${type}`
            : 'field-type-input';

    const fieldTypeBase = {
        props: {
            // internal
            path: Array,
            // field config
            type: String,
            constant: Boolean,
            options: Array,
            fields: Array,
            name: String,
            label: String,
            value: String,
            helpText: String,
            required: Boolean
        }
    }

    const FieldTypeInput = Vue.extend({
        mixins: [fieldTypeBase],
        name: 'field-type-input',
        template: '#field-type-input'
    })

    const FieldTypeTextarea = Vue.extend({
        mixins: [fieldTypeBase],
        name: 'field-type-textarea',
        template: '#field-type-textarea'
    })

    const FieldTypeSelect = Vue.extend({
        mixins: [fieldTypeBase],
        name: 'field-type-select',
        template: '#field-type-select',
        props: {
            options: Array
        }
    })
    
    const components = {
        FieldTypeInput,
        FieldTypeSelect,
        FieldTypeTextarea
    }

    // register fields-editor and field-type-fieldset globally in order to use them recursively
    Vue.component('field-type-fieldset', {
        mixins: [fieldTypeBase],
        template: '#field-type-fieldset',
        components,
        props: {
            fields: Array
        }
    })

    Vue.component('fields-editor', {
        template: '#fields-editor',
        components,
        props: {
            path: Array,
            fields: Array
        },
        methods: {
            getFieldComponent
        }
    })

    new Vue({
        el: '#FormEditor',
        name: 'form-editor',
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
            updateFromJSON(event) {
                const config = parseConfig(event.target.value)
                if (config) this.config = config
            },
            removeField(event, path, index) {
                const fields = this.getFieldsForPath(path)
                fields.splice(index, 1)
            },
            sortFields(event, path) {
                const { newIndex, oldIndex } = event
                const fields = this.getFieldsForPath(path)
                fields.splice(newIndex, 0, fields.splice(oldIndex, 1)[0])
            },
            getFieldsForPath (path) {
                let fields = this.config.fields
                while (path.length) {
                    const name = path.shift()
                    fields = fields.find(field => field.name === name).fields
                }
                return fields
            }
        }
    })
})
