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
    
    const specialFieldTypeOptions = ['fieldset', 'textarea', 'select']
    const inputFieldTypeOptions = ['text', 'number', 'password', 'email', 'url', 'tel', 'date', 'hidden']
    const fieldTypeOptions = inputFieldTypeOptions.concat(specialFieldTypeOptions)

    const getFieldComponent = type => `field-type-${specialFieldTypeOptions.includes(type) ? type : 'input'}`
    
    const fieldProps = {
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
    
    const fieldTypeBase = {
        props: {
            // internal
            path: Array,
            // field config
            ...fieldProps
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
            fields: Array,
            selectedField: fieldProps
        }
    })

    Vue.component('fields-editor', {
        template: '#fields-editor',
        components,
        props: {
            path: Array,
            fields: Array,
            selectedField: fieldProps
        },
        methods: {
            getFieldComponent
        }
    })

    Vue.component('field-editor', {
        template: '#field-editor',
        components,
        data () {
            return {
                fieldTypeOptions
            }
        },
        props: {
            path: Array,
            field: fieldProps
        },
        methods: {
            getFieldComponent
        }
    })

    Vue.use(vSortable)

    new Vue({
        el: '#FormEditor',
        name: 'form-editor',
        data () {
            return {
                config,
                selectedField: null
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
                this.selectedField = null
            },
            updateFromJSON(event) {
                const config = parseConfig(event.target.value)
                if (!config) return
                this.config = config
                this.selectedField = null
            },
            selectField(event, path, index) {
                const fields = this.getFieldsForPath(path)
                this.selectedField = fields[index]
            },
            removeField(event, path, index) {
                const fields = this.getFieldsForPath(path)
                fields.splice(index, 1)
                this.selectedField = null
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
