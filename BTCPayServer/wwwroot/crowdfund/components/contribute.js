Vue.component('contribute', {
    props: ["targetCurrency", "active", "perks", "inModal"],
    template: "#contribute-template",
    data: function () {
        return {
            email: "",
            amount: 0,
            choiceKey: ""
        }
    },
    methods: {
        onContributeFormSubmit: function (e) {
            if (e) {
                e.preventDefault();
            }
            if(!this.active){
                return;
            }
            eventAggregator.$emit("contribute", {email: this.email, amount: this.amount, choiceKey: this.choiceKey});
        }
    }
});

Vue.component('perks-display', {
    props: ["perks"],
    template: "#perks-template",
    data: function () {
        return {
        }
    },
    methods: {
        
    }
});


