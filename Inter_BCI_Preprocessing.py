from pyxdf import match_streaminfos, resolve_streams
from mnelab.io.xdf import read_raw_xdf

import mne
import numpy as np
import matplotlib as mpl
from mne.preprocessing import ICA, create_eog_epochs
import os

mpl.use('TkAgg')  # interactive plotting backend or can use 'Qt5Agg', whatever you have/prefer

# ------------------------ Read the file ---------------------
# make sure you have this file (your recording in xdf) in your working directory
# file_name = "sub-P001_ses-S001_task-Default_run-001_eeg (1).xdf"
file_name = "00002sub-P001_ses-S001_task-Default_run-001_eeg"

streams = resolve_streams(file_name + '.xdf')
stream_id = match_streaminfos(streams, [{"type": "EEG"}])[0]
raw = read_raw_xdf(file_name + '.xdf', stream_ids=[stream_id])

# if the file is in .fif format, simply use
# raw = mne.io.read_raw(file_name + ".fif", preload=True)

# ------------------------ First preprocessing ---------------------
# drop all channels above 20
list = raw.ch_names[0:32:1]
raw.drop_channels(list[20:])
raw.set_montage("easycap-M1")

# drop breaks if not done already
break_annots = mne.preprocessing.annotate_break(
    raw=raw,
    min_break_duration=10,  # consider segments of at least 20 s duration
    t_start_after_previous=0.5,  # start annotation 5 s after end of previous one
    t_stop_before_next=0.5,  # stop annotation 2 s before beginning of next one
)
raw.set_annotations(raw.annotations + break_annots)  # add to existing

# Handling eye blink artifacts
eye_chs = raw.ch_names[0:3:1]

artifact_picks = mne.pick_channels(raw.ch_names, include=eye_chs)
raw.plot(order=artifact_picks, n_channels=len(artifact_picks), title='Eye channel raw')
eog_evoked = create_eog_epochs(raw, ch_name=eye_chs).average()
eog_evoked.apply_baseline(baseline=(None, -0.2))
eog_evoked.plot_joint(title='Eye blinks')

# ------------------------ ICA --------------------------------
filt_raw = raw.copy().filter(l_freq=1.0, h_freq=40)
ica = ICA(n_components=20, max_iter="auto", random_state=97, method='infomax', )
rejects = dict(eeg=200e-6)
ica.fit(filt_raw)#, reject=rejects)
ica.plot_components(title='ICA components of ' + file_name, colorbar=True, inst=True)
# compare these signals
ica.plot_sources(raw, title='ICA source signals')
raw.plot()

# can use ICA indices to manually omit --> double check which ICA components correspond to eye blinks
# used to investigate individual components
print('Properties of picked ICA components')
ica.plot_properties(raw, picks=[0, 1, 4])
ica.exclude = [0]

# double-check for blinks
ica.plot_overlay(raw, exclude=[0, 5], picks="eeg")
# find which ICs match the EOG pattern automatically
eog_indices, eog_scores = ica.find_bads_eog(raw, ch_name=eye_chs)
# barplot of ICA component "EOG match" scores
ica.plot_scores(eog_scores)#, exclude=[0, 5])
ica.plot_sources(eog_evoked)

ica.apply(raw)
raw.save("ICA_applied2_" + file_name + ".fif", overwrite=True)

# ------------------------ Event creation ---------------------
raw = mne.io.read_raw("ICA_applied2_" + file_name + ".fif", preload=True)
raw.filter(l_freq=1, h_freq=10, fir_design='firwin', verbose=False)
events = mne.events_from_annotations(raw)

# if necessary, manually add the E0 marker locations to the events
# event_copy = events
# event_new_marker = mne.pick_events(event_copy[0], include=[1, 2, 5])
#
# error_found = False
# i = 0
# while i < len(event_new_marker):
#     if event_new_marker[i][2] < 3:
#         error_found = True
#     elif event_new_marker[i][2] == 5:
#         if not error_found:
#             event_new_marker = np.concatenate((event_new_marker[:i], [[event_new_marker[i][0], 0, 0]], event_new_marker[i:]))
#             # print(event_new_marker)
#             i += 1
#         else:
#             error_found = False
#     i += 1
#
# event_dict = {'No Error Trial': 0,
#               'Freezing Error Trial': 1,
#               'Feedback Error Trial': 2,
#               'End of Trial': 5}
# event_copy = (event_new_marker, event_dict)
#
# # map the new complete marker list
# mapping = {0: 'No Error Trial',
#            1: 'Freezing Error Trial',
#            2: 'Feedback Error Trial',
#            5: 'End of Trial'}
#
# # and apply them to the annotations object
# annot_from_events = mne.annotations_from_events(event_copy[0], event_desc=mapping, sfreq=raw.info["sfreq"],
#                                                 orig_time=raw.info["meas_date"])
# raw.set_annotations(raw.annotations + annot_from_events)
# raw.plot()
raw.save("ICA_applied2_" + file_name + ".fif", overwrite=True)

# ------------------------ Epoch creation ---------------------
# raw = mne.io.read_raw("everyone.fif")
tmin = -.30
tmax = 1
# ind. sets
event_dict = {'No Error Trial': 1,
              'Freezing Error Trial': 2,
              'Feedback Error Trial': 3,
              'End of Trial': 5}

# for group set
event_dict = {'No Error Trial': 4,
              'Freezing Error Trial': 3,
              'Feedback Error Trial': 2,
              'End of Trial': 1}

# check for other rejection criteria as well
reject_criteria = dict(eeg=200e-6)

# all (but unbalanced) data
epochs = mne.Epochs(raw, events[0], event_id=event_dict, tmin=tmin, tmax=tmax, reject=reject_criteria, preload=True,
                    event_repeated='merge', reject_tmin=0.4)
epochs.plot()
epochs.equalize_event_counts(['No Error Trial', ['Freezing Error Trial', 'Feedback Error Trial']])

# ------------------------ ERRP creation ---------------------
Ne = epochs['No Error Trial'].average()
Ne.comment = "No Error"
Te = epochs['Freezing Error Trial'].average()
Te.comment = "Tracking Error"
Fe = epochs['Feedback Error Trial'].average()
Fe.comment = "Feedback Error"
evokeds = [Ne, Te, Fe]

# grand averages
times = np.arange(-0.3, tmax, 0.1)
roi2 = ['Fz', 'FC1', 'FCz', 'FC2', 'Cz']

# No error
Ne.plot_joint(title='No Error', exclude=['AF3', 'AFz', 'AF4', 'F3', 'F1', 'F2', 'F4', 'FC3', 'FC4', 'C3', 'C4', 'CP1',
                                         'CPz', 'CP2', 'Pz'])
Ne.plot_topomap(times=times, ncols="auto", nrows=2)
Ne.plot_joint(times=times, title='No Error', picks=roi2)

# Tracking Error
Te.plot_joint(times=times, title='Tracking Error', picks='FCz')
Te.plot_joint(title='Tracking Error', exclude=['AF3', 'AFz', 'AF4', 'F3', 'F1', 'F2', 'F4', 'FC3', 'FC4', 'C3', 'C4',
                                               'CP1', 'CPz', 'CP2', 'Pz'])
Te.plot_topomap(times=times, ncols="auto", nrows=2)

# Feedback Error
Fe.plot_joint(times=times, title='Feedback Error', picks='FCz')
Fe.plot_joint(title='Feedback Error', exclude=['AF3', 'AFz', 'AF4', 'F3', 'F1', 'F2', 'F4', 'FC3', 'FC4', 'C3', 'C4',
                                               'CP1', 'CPz', 'CP2', 'Pz'])
Fe.plot_topomap(times=times, ncols="auto", nrows=2)

# Plot ErrP in FCz
linestyle_dict = {'Tracking Error': '--', 'Feedback Error': '-.'}
mne.viz.plot_compare_evokeds(evokeds,
                             picks='FCz', legend='lower right', show_sensors='lower left',
                             linestyles=linestyle_dict, title='Grand signal averages at Channel FCz')
# Plot ErrP in area of interest
roi = ['Fz', 'FC1', 'FCz', 'FC2', 'Cz']
mne.viz.plot_compare_evokeds(evokeds,
                             picks=roi, combine='mean', legend='lower right', show_sensors='lower left',
                             linestyles=linestyle_dict, title='Grand signal averages at Channels ' + str(roi))

# Difference waves
evokeds_TeNe = mne.combine_evoked([evokeds[1], evokeds[0]], weights=[1, -1])
evokeds_FeNe = mne.combine_evoked([evokeds[2], evokeds[0]], weights=[1, -1])
times = np.arange(0, evokeds_TeNe.tmax, 0.1)

ev_diffs = [evokeds_TeNe, evokeds_FeNe]
mne.viz.plot_compare_evokeds(ev_diffs,
                             picks=roi, legend='lower right', show_sensors='upper right', combine='mean',
                             title='Difference Waves: ' + str(roi))
evokeds_TeNe.plot_topomap(times=times, average=0.050)
evokeds_FeNe.plot_topomap(times=times, average=0.050)

# -------------------------- Appending ----------------------------------


def append_all():
    # put the folder path to your individual data sets in here
    os.chdir('H:/University of Groningen/EEG Data/Brand Nowicki Study Results/Individual datasets')
    all_part_path = os.listdir('.')

    # read one data file and append the others after - careful not to append the first one twice
    raw = mne.io.read_raw("ICA_applied2_00002sub-P001_ses-S001_task-Default_run-001_eeg.fif")
    all_in_one = raw.copy()
    all_in_one.load_data()
    all_in_one.resample(500)

    for raws in range(1, len(all_part_path)):
        raw = mne.io.read_raw(all_part_path[raws])
        raw.load_data()
        raw.resample(500)
        all_in_one.append(raw)

    all_in_one.plot()
    all_in_one.save("everyone.fif", overwrite=True)


