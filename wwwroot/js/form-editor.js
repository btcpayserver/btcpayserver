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
    
    const specialFieldTypeOptions = ['fieldset', 'textarea', 'select', 'mirror']
    const inputFieldTypeOptions = ['text', 'number', 'password', 'email', 'url', 'tel', 'date', 'datetime-local', 'color', 'checkbox', 'hidden']
    const fieldTypeOptions = inputFieldTypeOptions.concat(specialFieldTypeOptions)

    const getFieldComponent = type => `field-type-${specialFieldTypeOptions.includes(type) ? type : 'input'}`
    
    const fieldProps = {
        type: String,
        name: String,
        label: String,
        value: String,
        helpText: String,
        required: Boolean,
        constant: Boolean,
        options: Array,
        fields: Array,
        validationErrors: Array
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
    const FieldTypeMirror = Vue.extend({
        mixins: [fieldTypeBase],
        name: 'field-type-mirror',
        template: '#field-type-mirror'
    })
    
    const components = {
        FieldTypeInput,
        FieldTypeSelect,
        FieldTypeTextarea,
        FieldTypeMirror
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
        computed: {
            mirroredField() {
                return this.field.type === 'mirror' &&
                    this.$root.allFields.find(f => f.name === this.field.value)
            }
        },
        methods: {
            getFieldComponent,
            addOption (event) {
                if (!this.field.options) this.$set(this.field, 'options', [])
                const index = this.field.options.length + 1
                this.field.options.push({ value: `newOption${index}`, text: `New option ${index}` })
            },
            removeOption(event, index) {
                this.field.options.splice(index, 1)
            },
            sortOptions (event) {
                const { newIndex, oldIndex } = event
                this.field.options.splice(newIndex, 0, this.field.options.splice(oldIndex, 1)[0])
            },
            addValueMap (event) {
                if (!this.field.valuemap) this.$set(this.field, 'valuemap', {})
                const index = Object.keys(this.field.valuemap).length + 1;
                this.$set(this.field.valuemap, `valuemap_${index}`, '')
            },
            updateValueMap(oldK, newK, newV) {
                if (oldK !== newK) {
                    Vue.delete(this.field.valuemap, oldK);
                }
                Vue.set(this.field.valuemap, newK, newV);
            },
            removeValueMap(event, k) {
                Vue.delete(this.field.valuemap, k);
            },
        }
    })

    Vue.use(vSortable)
    Vue.use(VueSanitizeDirective.default)

    new Vue({
        el: '#FormEditor',
        name: 'form-editor',
        data () {
            return {
                config,
                selectedField: null,
                editorOffcanvas: null
            }
        },
        computed: {
            allFields() {
                const getFields = (fields, path) => {
                    let result = [];
                    for (const field of fields) {
                        result.push(field)
                        if (field.fields && field.fields.length > 0)
                            result= result.concat(getFields(field.fields, path + field.name));
                    }
                    return result;
                }
                return getFields(this.fields, "")
            },
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
            addField(event, path) {
                const fields = this.getFieldsForPath(path)
                const index = fields.length + 1
                const length = fields.push({ type: 'text', name: `newField${index}`, label: `New field ${index}`, fields: [], options: [] })
                this.selectedField = fields[length - 1]
                this.showOffcanvas()
            },
            selectField(event, path, index) {
                const fields = this.getFieldsForPath(path)
                this.selectedField = fields[index]
                this.showOffcanvas()
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
                if (!this.config.fields) this.$set(this.config, 'fields', [])
                let fields = this.config.fields
                while (path.length) {
                    const name = path.shift()
                    const field = fields.find(field => field.name === name)
                    if (!field.fields) this.$set(field, 'fields', [])
                    fields = field.fields
                }
                return fields
            },
            showOffcanvas() {
                if (this.editorOffcanvas && window.getComputedStyle(this.$refs.editorOffcanvas).visibility === 'hidden')
                    this.editorOffcanvas.show();
            },
            hideOffcanvas() {
                if (this.editorOffcanvas)
                    this.editorOffcanvas.hide();
            }
        },
        mounted () {
            if (!this.config.fields || this.config.fields.length === 0) {
                this.addField(null,[])
            }
            this.editorOffcanvas = bootstrap.Offcanvas.getOrCreateInstance(this.$refs.editorOffcanvas);
        }
    })
})
