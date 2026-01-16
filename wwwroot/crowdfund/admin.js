const resetRow = document.getElementById('ResetRow');
const startDateInputId = "StartDate";

document.addEventListener("DOMContentLoaded", () => {
    setTimeout(() => {
        flatpickrInstances.forEach((instance) => {
            if (instance.element.id === startDateInputId) {
                instance.config.onChange.push((selectedDates) => {
                    if (selectedDates.length) {
                        // Show the reset row if start date is selected.
                        // Since start date must be selected in order for the reset options to be set
                        // we don't need to show it by default and can show it only when start date is selected
                        resetRow.removeAttribute('hidden');
                    }
                });
            }
        });
    }, 0);

    document.addEventListener('input-group-clear-input-value-cleared', ({ detail }) => {
        const input = detail[0];
        if (input.id === startDateInputId) {
            resetRow.setAttribute('hidden', 'hidden');
        }
    });
});
