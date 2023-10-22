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
    
    Vue.component('items-editor', {
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

    Vue.component('item-editor', {
        template: '#item-editor',
        components: {
            Item
        },
        props: {
            item: itemProps
        },
        data () {
            return {
                errors: [],
                editingItem: null,
                uploadError: null,
                uploadDisabled: true,
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
            validate () {
                this.errors = [];
                
                if (this.editingItem.id) {
                    const existingItem = this.$parent.items.find(i => i.id === this.editingItem.id);
                    if (existingItem && existingItem.id !== this.item.id)
                        this.errors.push(`An item with the ID "${this.editingItem.id}" already exists`);
                    if (this.editingItem.id.startsWith('-'))
                        this.errors.push('ID cannot start with "-"');
                    else if (this.editingItem.id.trim() === '')
                        this.errors.push('ID is required');
                } else {
                    this.errors.push('ID is required');
                }
                
                const { inputTitle, inputPrice, inputInventory } = this.$refs
                Object.keys(this.$refs).forEach(ref => {
                    if (ref.startsWith('input')) {
                        const $ref = this.$refs[ref];
                        $ref.classList.toggle('is-invalid', !$ref.checkValidity())
                    }
                })
                
                if (this.editingItem.priceType !== 'Topup' && !inputPrice.checkValidity()) {
                    this.errors.push('Price must be a valid number');
                }
                
                if (!inputTitle.checkValidity()) {
                    this.errors.push('Title is required');
                } else if (this.editingItem.title.startsWith('-')){
                    this.errors.push('Title cannot start with "-"');
                } else if (!this.editingItem.title.trim()){
                    this.errors.push('Title is required');
                }
                
                if (!inputInventory.checkValidity()) {
                    this.errors.push('Inventory must not be set or be a valid number (>=0)');
                }
                
                return this.errors.length === 0;
            },
            apply() {
                // set id from title if not set
                if (!this.editingItem.id) this.editingItem.id = this.editingItem.title.toLowerCase().trim();
                // validate
                if (!this.validate()) return;
                // set item props
                Object.keys(this.editingItem).forEach(prop => {
                    const value = this.editingItem[prop];
                    Vue.set(this.$parent.selectedItem, prop, value);
                })
                // update categories
                this.categoriesSelect.clearOptions();
                this.categoriesSelect.addOptions(this.allCategories.map(value => ({ value, text: value })));
            },
            uploadFileChanged () {
                this.uploadDisabled = !this.$refs.editorImage || this.$refs.editorImage.files.length === 0;
            },
            async uploadFile() {
                const file = this.$refs.editorImage.files[0];
                if (!file) return this.uploadError = 'No file selected';
                this.uploadError = null;
                const formData = new FormData();
                formData.append('file', file);
                try {
                    const response = await fetch(fileUploadUrl, { method: 'POST', body: formData });
                    if (response.ok) {
                        const { error, fileUrl } = await response.json();
                        if (error) return this.uploadError = error;
                        this.editingItem.image = fileUrl;
                        this.$refs.editorImage.value = null;
                        this.uploadDisabled = true;
                        return;
                    }
                } catch (e) {
                    console.error(e);
                }
                this.uploadError = 'Upload failed';
            }
        },
        watch: {
            item: function (newItem) {
                this.errors = [];
                this.editingItem = newItem ? { ...newItem } : null;
                if (this.editingItem != null) {
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
                this.editingItem.categories = Array.from(value.split(',').reduce((res,  item) => {
                    const category = item.trim();
                    if (category) res.add(category);
                    return res;
                }, new Set()));
            });
        },
        beforeDestroy() {
            this.categoriesSelect.destroy();
        }
    })

    Vue.use(vSortable)
    Vue.use(VueSanitizeDirective.default)

    new Vue({
        el: '#TemplateEditor',
        name: 'template-editor',
        data () {
            return {
                items,
                selectedItem: null
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
            }
        },
        methods: {
            updateFromJSON(event) {
                const items = parseConfig(event.target.value)
                if (!items) return
                this.items = items
                this.selectedItem = null
            },
            addItem(event) {
                const index = this.items.length + 1
                const length = this.items.push({ id: '', title: '', price: 0, image: '', description: '', categories: [], priceType: 'Fixed', inventory: null, disabled: false })
                this.selectedItem = this.items[length - 1]
            },
            selectItem(event, index) {
                this.selectedItem = this.items[index]
            },
            removeItem(event, index) {
                this.items.splice(index, 1)
                this.selectedItem = null
            },
            sortItems(event) {
                const { newIndex, oldIndex } = event
                this.items.splice(newIndex, 0, this.items.splice(oldIndex, 1)[0])
            }
        },
        mounted() {
            if (!this.items) this.items = []
        }
    })
})
