document.addEventListener('DOMContentLoaded', () => {
    const parseConfig = str => {
        try {
            return JSON.parse(str)
        } catch (err) {
            console.error('Error deserializing form config:', err)
        }
    }
    const $config = document.getElementById('TemplateConfig')
    let items = parseConfig($config.value) || []
    
    const itemProps = {
        id: String,
        title: String,
        image: String,
        description: String,
        priceType: String,
        price: Number,
        inventory: Number,
        disabled: Boolean,
        categories: Array
    }

    const Item = Vue.extend({
        name: 'item',
        template: '#item',
        props: {
            ...itemProps
        }
    })

    const ItemEditorUpload = Vue.component('item-editor-upload', {
        template: '#item-editor-upload',
        props: {
            uploadUrl: {
                type: String,
                required: true
            }
        },
        data () {
            return {
                error: null,
                disabled: true
            }
        },
        methods: {
            fileChanged () {
                this.disabled = !this.$refs.input || this.$refs.input.files.length === 0;
            },
            reportError(error) {
                this.error = error;
                this.$refs.input.classList.add('is-invalid');
                this.$emit('error', error);
            },
            async upload() {
                const file = this.$refs.input.files[0];
                if (!file) return this.reportError('No file selected');
                this.error = null;
                this.$refs.input.classList.remove('is-invalid');
                const formData = new FormData();
                formData.append('file', file);
                try {
                    const response = await fetch(this.uploadUrl, { method: 'POST', body: formData });
                    if (response.ok) {
                        const { error, fileUrl } = await response.json();
                        if (error) {
                            this.reportError(error)
                        } else {
                            this.$refs.input.value = null;
                            this.disabled = true;
                            this.$emit('uploaded', fileUrl);
                        }
                    }
                } catch (e) {
                    console.error(e);
                    this.reportError('Upload failed');
                }
            }
        }
    })

    const ItemEditor = Vue.component('item-editor', {
        template: '#item-editor',
        components: {
            Item,
            ItemEditorUpload
        },
        props: {
            item: itemProps
        },
        data () {
            return {
                errors: {},
                editingItem: null,
                categoriesSelect: null,
                customPriceOptions: [
                    { text: 'Fixed', value: 'Fixed' },
                    { text: 'Minimum', value: 'Minimum' },
                    { text: 'Custom', value: 'Topup' },
                ]
            }
        },
        computed: {
            allCategories() {
                return this.$parent.allCategories;
            }
        },
        methods: {
            toId(value) {
                return value.toLowerCase().trim().replace(/\W+/gi, '-')
            },
            onTitleChange(e) {
                const $input = e.target;
                $input.classList.toggle('is-invalid', !$input.checkValidity())
                if (!$input.checkValidity()) {
                    Vue.set(this.errors, 'title', 'Title is required')
                } else if (this.editingItem.title.startsWith('-')){
                    Vue.set(this.errors, 'title', 'Title cannot start with "-"')
                } else if (!this.editingItem.title.trim()){
                    Vue.set(this.errors, 'title', 'Title is required')
                } else {
                    Vue.delete(this.errors, 'title')
                }
                // set id from title if not set
                if (!this.editingItem.id) {
                    this.editingItem.id = this.toId(this.editingItem.title)
                    Vue.delete(this.errors, 'id')
                }
            },
            onIdChange(e) {
                // set id from title if not set
                if (!this.editingItem.id) this.editingItem.id = this.toId(this.editingItem.title)
                // validate
                const $input = e.target;
                $input.classList.toggle('is-invalid', !$input.checkValidity())
                if (this.editingItem.id) {
                    const existingItem = this.$parent.items.find(i => i.id === this.editingItem.id);
                    if (existingItem && existingItem.id !== this.item.id)
                        Vue.set(this.errors, 'id', `An item with the ID "${this.editingItem.id}" already exists`)
                    if (this.editingItem.id.startsWith('-'))
                        Vue.set(this.errors, 'id', 'ID cannot start with "-"')
                    else if (this.editingItem.id.trim() === '')
                        Vue.set(this.errors, 'id', 'ID is required')
                    else
                        Vue.delete(this.errors, 'id')
                } else {
                    Vue.set(this.errors, 'id', 'ID is required')
                }
            },
            onInventoryChange(e) {
                const $input = e.target;
                $input.classList.toggle('is-invalid', !$input.checkValidity())
                if (!$input.checkValidity()) {
                    Vue.set(this.errors, 'inventory', 'Inventory must not be set or be a valid number (>=0)')
                }
            },
            onPriceChange(e) {
                const $input = e.target;
                $input.classList.toggle('is-invalid', !$input.checkValidity())
                if (this.editingItem.priceType !== 'Topup' && !$input.checkValidity()) {
                     Vue.set(this.errors, 'price', 'Price must be a valid number')
                } else {
                    Vue.delete(this.errors, 'price')
                }
            },
            onPriceTypeChange(e) {
                const $input = e.target;
                $input.classList.toggle('is-invalid', !$input.checkValidity())
                if ($input.value === 'Topup') {
                    Vue.set(this.editingItem, 'price', null)
                }
            }
        },
        watch: {
            item(newItem) {
                this.errors = {};
                this.editingItem = newItem;
                if (this.editingItem != null) {
                    // update categories
                    this.categoriesSelect.clearOptions();
                    this.categoriesSelect.addOptions(this.allCategories.map(value => ({ value, text: value })));
                    this.categoriesSelect.setValue(this.editingItem.categories);
                }
            }
        },
        mounted() {
            this.categoriesSelect = new TomSelect(this.$refs.editorCategories, {
                persist: false,
                createOnBlur: true,
                create: true,
                options: this.allCategories.map(value => ({ value, text: value })),
            });
            this.categoriesSelect.on('change', () => {
                const value = this.categoriesSelect.getValue();
                Vue.set(this.editingItem, 'categories', Array.from(value.split(',').reduce((res,  item) => {
                    const category = item.trim();
                    if (category) res.add(category);
                    return res;
                }, new Set())))
            });
        },
        beforeDestroy() {
            this.categoriesSelect.destroy();
        }
    })

    const ItemsEditor = Vue.component('items-editor', {
        template: '#items-editor',
        components: {
            Item
        },
        props: {
            items: Array,
            selectedItem: itemProps
        },
        methods: {
            getImage(item) {
                const image = item.image || '~/img/img-placeholder.svg';
                return image.startsWith('~') ? image.replace('~', window.location.pathname.substring(0, image.indexOf('/apps'))) : image
            }
        }
    })

    Vue.use(vSortable)
    Vue.use(VueSanitizeDirective.default)

    new Vue({
        el: '#TemplateEditor',
        name: 'template-editor',
        components: {
            ItemsEditor,
            ItemEditor
        },
        data () {
            return {
                items,
                selectedItem: null,
                selectedItemInitial: null,
                editorOffcanvas: null,
            }
        },
        computed: {
            itemsJSON() {
                return JSON.stringify(this.items, null, 2)
            },
            allCategories() {
                return Array.from(this.items.reduce((res,  item) => {
                    (item.categories || []).forEach(category => { res.add(category); });
                    return res;
                }, new Set()));
            },
            itemChanged() {
                return (!this.selectedItem && this.selectedItemInitial) ||
                    (!this.selectedItem && this.selectedItemInitial) ||
                    (JSON.stringify(this.selectedItem) !== JSON.stringify(this.selectedItemInitial))
                
            }
        },
        methods: {
            updateFromJSON(event) {
                const items = parseConfig(event.target.value)
                if (!items) return
                this.items = items
                this.selectedItem = this.selectedItemInitial = null
            },
            addItem(event) {
                const length = this.items.push({
                    id: '',
                    title: '',
                    priceType: 'Fixed',
                    price: 0,
                    image: '',
                    description: '',
                    categories: [],
                    inventory: null,
                    disabled: false
                })
                this.selectItem(null, length - 1)
            },
            selectItem(event, index) {
                this.selectedItem = this.items[index]
                this.selectedItemInitial = { ...this.selectedItem } // pristine copy
                this.showOffcanvas()
            },
            removeItem(event, index) {
                this.items.splice(index, 1)
                this.selectedItem = this.selectedItemInitial = null
            },
            sortItems(event) {
                const { newIndex, oldIndex } = event
                this.items.splice(newIndex, 0, this.items.splice(oldIndex, 1)[0])
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
        mounted() {
            if (!this.items) this.items = []
            this.editorOffcanvas = bootstrap.Offcanvas.getOrCreateInstance(this.$refs.editorOffcanvas);
        }
    })
})
