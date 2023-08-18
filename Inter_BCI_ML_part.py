import mne
import numpy as np
import pandas as pd
import time
import matplotlib as mpl
import matplotlib.pyplot as plt
from sklearn.model_selection import train_test_split
from sklearn.discriminant_analysis import LinearDiscriminantAnalysis
from sklearn.model_selection import cross_val_score, cross_validate
from sklearn.metrics import confusion_matrix, ConfusionMatrixDisplay, classification_report
from sklearn.dummy import DummyClassifier
from sklearn.ensemble import RandomForestClassifier, AdaBoostClassifier
from scipy import stats

mpl.use('TkAgg')  # or can use 'Qt5Agg', whatever you have/prefer

raw = mne.io.read_raw("everyone.fif")

events = mne.events_from_annotations(raw)

event_dict = {'No Error Trial': 4,
              'Freezing Error Trial': 3,
              'Feedback Error Trial': 2,
              'End of Trial': 1}

# 600ms epochs 150ms before error onset or feedback
tmin = -.15
tmax = 1

# check for other rejection criteria as well
reject_criteria = dict(eeg=200e-6)

# all (but unbalanced) data
epochs = mne.Epochs(raw, events[0], event_id=event_dict, tmin=tmin, tmax=tmax, reject=reject_criteria, preload=True,
                    event_repeated='merge')
# epochs.plot()
epochs.equalize_event_counts(['No Error Trial', ['Freezing Error Trial', 'Feedback Error Trial']])
print('done')

# ################### ML PART ################################ #
# data = [[ch], [epochs], [times]]
data = epochs.get_data(units='uV')

# ------------ Grand Prep ---------------------
# target values/labels = [0 - no error
#                         1 - tracking error
#                         2 - feedback error]
labels = np.empty([epochs.events.shape[0]], dtype=int)
# labels.shape == n_samples/n_epochs.size
i = 0
while i < len(labels):
    # No error
    if epochs.events[i][2] == 4 or epochs.events[i][2] == 5:
        labels[i] = 0
    # Tracking error
    elif epochs.events[i][2] == 3 or epochs.events[i][2] == 7:
        labels[i] = 1
    # Feedback error
    elif epochs.events[i][2] == 2 or epochs.events[i][2] == 6:
        labels[i] = 2
    else:
        labels[i] = -1
    i += 1

# features = [[ch], [epoch_dur]]
all_features = np.empty([data.shape[0], data.shape[1]], dtype=np.float64)
all_features = data.reshape((data.shape[0], data.shape[1] * data.shape[2]))

# alternative, manual transformations - okay-ish results
# all_features = np.empty([data.shape[0], data.shape[1] * 5], dtype=np.float64)
# for i in range(0, data.shape[0]):
#     for j in range(0, data.shape[1]):
#         all_features[i][j * 5] = np.average(data[i][j])
#         all_features[i][j * 5 + 1] = np.max(data[i][j])
#         all_features[i][j * 5 + 2] = np.min(data[i][j])
#         all_features[i][j * 5 + 3] = np.var(data[i][j])
#         all_features[i][j * 5 + 4] = np.std(data[i][j])
# check features.shape == n_epochs * features

clf = LinearDiscriminantAnalysis(solver='lsqr', shrinkage='auto')
clf1 = RandomForestClassifier(max_features="sqrt")
clf2 = AdaBoostClassifier()
clf.fit(all_features, labels)
clf1.fit(all_features, labels)
clf2.fit(all_features, labels)
# scores = cross_val_score(clf, all_features, labels, cv=10)
scorers = ['balanced_accuracy', 'precision_weighted', 'recall_weighted', 'f1_weighted', 'roc_auc_ovr_weighted']
balanced_scores_all = cross_validate(clf, all_features, labels, cv=10, scoring=scorers)  # =scorers)
print("LDA Classifier: \n"
      "%0.3f balanced accuracy with a standard deviation of %0.3f\n"
      "%0.3f weighted precision with a standard deviation of %0.3f\n"
      "%0.3f weighted recall with a standard deviation of %0.3f\n"
      "%0.3f weighted f1 with a standard deviation of %0.3f\n"
      "%0.3f ROC with a standard deviation of %0.3f" %
      (balanced_scores_all['test_balanced_accuracy'].mean(), balanced_scores_all['test_balanced_accuracy'].std(),
       balanced_scores_all['test_precision_weighted'].mean(), balanced_scores_all['test_precision_weighted'].std(),
       balanced_scores_all['test_recall_weighted'].mean(), balanced_scores_all['test_recall_weighted'].std(),
       balanced_scores_all['test_f1_weighted'].mean(), balanced_scores_all['test_f1_weighted'].std(),
       balanced_scores_all['test_roc_auc_ovr_weighted'].mean(), balanced_scores_all['test_roc_auc_ovr_weighted'].std()))

balanced_scores_all1 = cross_validate(clf1, all_features, labels, cv=10, scoring=scorers)  # =scorers)
print("Random Forest Classifier: \n"
      "%0.3f balanced accuracy with a standard deviation of %0.3f\n"
      "%0.3f weighted precision with a standard deviation of %0.3f\n"
      "%0.3f weighted recall with a standard deviation of %0.3f\n"
      "%0.3f weighted f1 with a standard deviation of %0.3f\n"
      "%0.3f ROC with a standard deviation of %0.3f" %
      (balanced_scores_all1['test_balanced_accuracy'].mean(), balanced_scores_all1['test_balanced_accuracy'].std(),
       balanced_scores_all1['test_precision_weighted'].mean(), balanced_scores_all1['test_precision_weighted'].std(),
       balanced_scores_all1['test_recall_weighted'].mean(), balanced_scores_all1['test_recall_weighted'].std(),
       balanced_scores_all1['test_f1_weighted'].mean(), balanced_scores_all1['test_f1_weighted'].std(),
       balanced_scores_all1['test_roc_auc_ovr_weighted'].mean(), balanced_scores_all1['test_roc_auc_ovr_weighted'].std()))

balanced_scores_all2 = cross_validate(clf2, all_features, labels, cv=10, scoring=scorers)  # =scorers)
print("AdaBoost Classifier: \n"
      "%0.3f balanced accuracy with a standard deviation of %0.3f\n"
      "%0.3f weighted precision with a standard deviation of %0.3f\n"
      "%0.3f weighted recall with a standard deviation of %0.3f\n"
      "%0.3f weighted f1 with a standard deviation of %0.3f\n"
      "%0.3f ROC with a standard deviation of %0.3f" %
      (balanced_scores_all2['test_balanced_accuracy'].mean(), balanced_scores_all2['test_balanced_accuracy'].std(),
       balanced_scores_all2['test_precision_weighted'].mean(), balanced_scores_all2['test_precision_weighted'].std(),
       balanced_scores_all2['test_recall_weighted'].mean(), balanced_scores_all2['test_recall_weighted'].std(),
       balanced_scores_all2['test_f1_weighted'].mean(), balanced_scores_all2['test_f1_weighted'].std(),
       balanced_scores_all2['test_roc_auc_ovr_weighted'].mean(), balanced_scores_all2['test_roc_auc_ovr_weighted'].std()))

# Confusion matrix
train_data, test_data, train_labels, test_labels = train_test_split(data, labels)
train_data = train_data.reshape((train_data.shape[0], train_data.shape[1] * train_data.shape[2]))
test_data = test_data.reshape((test_data.shape[0], test_data.shape[1] * test_data.shape[2]))
clf1.fit(train_data, train_labels)
predicted = clf.predict(test_data)
cm = confusion_matrix(test_labels, predicted)
disp = ConfusionMatrixDisplay(confusion_matrix=cm, display_labels=['Invalid Data', 'No Error', 'Tracking Error', 'Feedback Error'])
disp.plot()
plt.show()

target_names = ['Invalid Data', 'No Error', 'Tracking Error', 'Feedback Error']
print(classification_report(test_labels, predicted, target_names=target_names))

# always predict most frequent (no error?) or uniform (50-50)?
dum_clf = DummyClassifier(strategy='uniform')  # strategy='uniform'
dum_clf.fit(train_data, train_labels)
dum_score = dum_clf.score(test_data, test_labels)
print("Dummy Classifier: %0.3f accuracy with a standard deviation of %0.3f" % (dum_score, dum_score.std()))
balanced_scores_dummy = cross_validate(dum_clf, all_features, labels, cv=10, scoring=scorers)  # =scorers)
print("Dummy Classifier: \n"
      "%0.2f balanced accuracy with a standard deviation of %0.2f\n"
      "%0.2f weighted precision with a standard deviation of %0.2f\n"
      "%0.2f weighted recall with a standard deviation of %0.2f\n"
      "%0.2f weighted f1 with a standard deviation of %0.2f\n"
      "%0.2f ROC with a standard deviation of %0.2f" %
      (balanced_scores_dummy['test_balanced_accuracy'].mean(), balanced_scores_dummy['test_balanced_accuracy'].std(),
       balanced_scores_dummy['test_precision_weighted'].mean(), balanced_scores_dummy['test_precision_weighted'].std(),
       balanced_scores_dummy['test_recall_weighted'].mean(), balanced_scores_dummy['test_recall_weighted'].std(),
       balanced_scores_dummy['test_f1_weighted'].mean(), balanced_scores_dummy['test_f1_weighted'].std(),
       balanced_scores_dummy['test_roc_auc_ovr_weighted'].mean(), balanced_scores_dummy['test_roc_auc_ovr_weighted'].std()))

# paired t-test
t_test = stats.ttest_rel(balanced_scores_all['test_balanced_accuracy'], balanced_scores_dummy['test_balanced_accuracy'],
                alternative='greater')

# feature importances for RF
start_time = time.time()
importances = clf1.feature_importances_
std = np.std([tree.feature_importances_ for tree in clf1.estimators_], axis=0)
elapsed_time = time.time() - start_time
feature_names = [f"feature {i}" for i in range(all_features.shape[1])]

print(f"Elapsed time to compute the importances: {elapsed_time:.3f} seconds")
forest_importances = pd.Series(importances, index=feature_names)

fig, ax = plt.subplots()
forest_importances.plot.bar(yerr=std, ax=ax)
ax.set_title("Feature importances using MDI")
ax.set_ylabel("Mean decrease in impurity")
ax.set_xlabel("Features")
# Major ticks every 20, minor ticks every 5
major_ticks = np.arange(0, importances.size, 1000)
minor_ticks = np.arange(0, importances.size, 500)
ax.set_xticks(major_ticks)
ax.set_xticks(minor_ticks, minor=True)
ax.tick_params(axis='x',          # changes apply to the x-axis
               which='both',      # both major and minor ticks are affected
               bottom=True,      # ticks along the bottom edge are off
               top=False,         # ticks along the top edge are off
               labelbottom=True) # labels along the bottom edge are off)
ax.set_xbound()
fig.tight_layout()
fig.show()
