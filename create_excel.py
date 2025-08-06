import pandas as pd
from openpyxl.styles import Font

# Read the CSV data
print('Reading CSV file...')
df = pd.read_csv('sample_riders.csv')

print(f'Loaded {len(df)} riders')
print(f'Columns: {list(df.columns)}')

# Display summary of classes
class_counts = df['class'].value_counts()
print('\nRiders per class:')
for class_name, count in class_counts.items():
    print(f'  {class_name}: {count} riders')

# Create Excel file with proper formatting
print('\nCreating Excel file...')
with pd.ExcelWriter('sample_riders.xlsx', engine='openpyxl') as writer:
    df.to_excel(writer, sheet_name='Riders', index=False)
    
    # Get the workbook and worksheet
    workbook = writer.book
    worksheet = writer.sheets['Riders']
    
    # Auto-adjust column widths
    for column in worksheet.columns:
        max_length = 0
        column_letter = column[0].column_letter
        for cell in column:
            try:
                if len(str(cell.value)) > max_length:
                    max_length = len(str(cell.value))
            except:
                pass
        adjusted_width = min(max_length + 2, 50)
        worksheet.column_dimensions[column_letter].width = adjusted_width
    
    # Make header row bold
    for cell in worksheet[1]:
        cell.font = Font(bold=True)

print('Excel file created successfully!')
print('Files available:')
print('  - sample_riders.csv (40 riders, 3 classes)')
print('  - sample_riders.xlsx (40 riders, 3 classes)')

# Display sample of data
print('\nFirst 5 riders:')
print(df.head().to_string())
