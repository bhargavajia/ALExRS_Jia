import matplotlib.pyplot as plt
import pandas as pd

# Read from csv file as pandas df as float
df = pd.read_csv(r"C:\Users\franc\Documents\Codebase\Data\data_20260210_121718.csv")

time = [i * 0.5 for i in range(len(df))]

# Plot the data
plt.subplot(2, 1, 1)
plt.plot(time, df['X_EE'], label='End effector X position')
plt.plot(time, df['Y_EE'], label='End effector Y position')
plt.plot(time, df['Z_EE'], label='End effector Z position')
plt.xlabel('Time (s)')
plt.ylabel('Position (m)')
plt.legend()
plt.title('End Effector Position Over Time')

plt.subplot(2, 1, 2)
plt.plot(time, df['Vel_Z_EE'])
plt.xlabel('Time (s)')
plt.ylabel('Velocity (m/s)')
plt.title('End Effector Velocity Over Time')
plt.tight_layout()
plt.show()





